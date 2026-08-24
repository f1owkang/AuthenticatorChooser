using System.Runtime.InteropServices;
using System.Text;

namespace PasskeyPick;

/// <summary>
/// Holds the USB security key's FIDO2 PIN in memory only — never written to disk — with a configurable TTL, modeled on
/// gpg-agent's passphrase cache (<c>default-cache-ttl</c>). The cached bytes are encrypted in place with
/// <c>CryptProtectMemory</c> (a per-process DPAPI session key), so the PIN is never stored in plaintext. On use the
/// bytes are decrypted into a temporary buffer (zeroed immediately) and materialized into a <c>BSTR</c> — an unmanaged
/// allocation outside the GC heap whose address changes on every use — which is passed to the native UI Automation
/// COM <c>IUIAutomationValuePattern::SetValue</c> and then zeroed with <c>RtlZeroMemory</c> before being freed, so no
/// plaintext copy ever exists as a managed string. The PIN must be re-entered after every program restart. The cache
/// refuses to store or decrypt the PIN while a debugger is attached, and forgets it instead.
/// </summary>
internal static class PinCache {

    private const uint CRYPTPROTECTMEMORY_SAME_PROCESS = 0x00;
    private const uint BLOCK_SIZE = 16; // CRYPTPROTECTMEMORY_BLOCK_SIZE

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(PinCache).FullName!);

    /// <summary>How long a cached PIN stays valid, in seconds, in [0, <see cref="Settings.MAX_TTL_SECONDS"/>]; 0 keeps
    /// it until the program exits rather than expiring on a timer.</summary>
    private static int ttlSeconds = Settings.DEFAULT_TTL_SECONDS;

    /// <summary>Whether the PIN must be forgotten when Windows locks, sleeps, or hibernates. All default on.</summary>
    private static bool clearOnLock      = true;
    private static bool clearOnSleep     = true;
    private static bool clearOnHibernate = true;

    /// <summary>The PIN encrypted with <see cref="CryptProtectMemory"/>; never holds plaintext.</summary>
    private static byte[]? encryptedPin;

    /// <summary>When the PIN was cached, on a monotonic clock (milliseconds) so system-clock changes cannot extend
    /// its life.</summary>
    private static long cachedAtMs;

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectMemory(IntPtr pDataIn, uint cbDataIn, uint dwFlags);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectMemory(IntPtr pDataIn, uint cbDataIn, uint dwFlags);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool isDebuggerPresent);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SysAllocString(IntPtr psz);

    [DllImport("oleaut32.dll")]
    private static extern void SysFreeString(IntPtr bstr);

    [DllImport("kernel32.dll")]
    private static extern void RtlZeroMemory(IntPtr destination, int length);

    /// <summary>Applies the persisted preferences. Call once at startup, before any FIDO dialogs may appear.</summary>
    public static void initialize() {
        ttlSeconds       = Settings.normalizeTtl(Settings.pinCacheTtlSeconds);
        clearOnLock      = Settings.pinClearOnLock;
        clearOnSleep     = Settings.pinClearOnSleep;
        clearOnHibernate = Settings.pinClearOnHibernate;
    }

    /// <summary>The cache TTL in seconds, clamped to [0, <see cref="Settings.MAX_TTL_SECONDS"/>], where 0 keeps the
    /// PIN until the program exits. Persisted on change.</summary>
    public static int ttlSecondsValue {
        get => ttlSeconds;
        set {
            ttlSeconds = Settings.normalizeTtl(value);
            Settings.pinCacheTtlSeconds = ttlSeconds;
            Settings.save();
        }
    }

    /// <summary>Whether the PIN must be forgotten when Windows locks. Persisted on change.</summary>
    public static bool clearOnLockEnabled {
        get => clearOnLock;
        set {
            clearOnLock = value;
            Settings.pinClearOnLock = value;
            Settings.save();
        }
    }

    /// <summary>Whether the PIN must be forgotten when Windows sleeps. Persisted on change.</summary>
    public static bool clearOnSleepEnabled {
        get => clearOnSleep;
        set {
            clearOnSleep = value;
            Settings.pinClearOnSleep = value;
            Settings.save();
        }
    }

    /// <summary>Whether the PIN must be forgotten when Windows hibernates. Persisted on change.</summary>
    public static bool clearOnHibernateEnabled {
        get => clearOnHibernate;
        set {
            clearOnHibernate = value;
            Settings.pinClearOnHibernate = value;
            Settings.save();
        }
    }

    /// <summary>Decrypts the cached PIN straight into a <c>BSTR</c> — an unmanaged allocation the GC never copies or
    /// moves — and hands it to <paramref name="use"/>, so the plaintext never exists as a managed string on the GC
    /// heap. The BSTR (including its length prefix) is zeroed with <c>RtlZeroMemory</c> before being freed, and every
    /// intermediate buffer is zeroed too. Returns <see langword="false"/> when no PIN is cached, it expired, a
    /// debugger is attached (the PIN is then forgotten), or <paramref name="use"/> itself failed.</summary>
    public static bool tryUseCachedPin(Func<IntPtr, bool> use) {
        if (debuggerAttached()) {
            LOGGER.Warn("A debugger is attached; forgetting the cached security key PIN instead of decrypting it");
            clear();
            return false;
        }
        if (encryptedPin is null) {
            return false;
        }
        if (isExpired()) {
            clear();
            return false;
        }
        byte[]? plain = decrypt(encryptedPin);
        if (plain is null) {
            return false;
        }
        try {
            // Padding bytes are zero; UTF-8 never contains NUL, so the first zero is the real end of the PIN.
            int length = Array.IndexOf(plain, (byte) 0);
            if (length < 0) {
                length = plain.Length;
            }
            char[] chars = Encoding.UTF8.GetChars(plain, 0, length);
            try {
                GCHandle pinnedChars = GCHandle.Alloc(chars, GCHandleType.Pinned);
                IntPtr   bstr;
                try {
                    bstr = SysAllocString(pinnedChars.AddrOfPinnedObject());
                } finally {
                    pinnedChars.Free();
                }
                if (bstr == IntPtr.Zero) {
                    return false;
                }
                try {
                    return use(bstr);
                } finally {
                    // A BSTR is a length-prefixed, NUL-terminated UTF-16 buffer: zero the prefix, the characters,
                    // and the terminator before releasing the allocation.
                    int charCount = Marshal.ReadInt32(bstr, -4);
                    RtlZeroMemory(bstr - 4, (charCount + 1) * 2 + 4);
                    SysFreeString(bstr);
                }
            } finally {
                Array.Clear(chars);
            }
        } finally {
            Array.Clear(plain);
        }
    }

    /// <summary>Whether a PIN is currently cached and still within its TTL. Checks only the timestamp and never
    /// decrypts, so opening the tray menu does not materialize a plaintext copy of the PIN. A debugger seen here
    /// still forgets the PIN, so merely watching the process cannot keep the cache alive.</summary>
    public static bool hasCached() {
        if (encryptedPin is null) {
            return false;
        }
        if (debuggerAttached()) {
            LOGGER.Warn("A debugger is attached; forgetting the cached security key PIN");
            clear();
            return false;
        }
        if (isExpired()) {
            clear();
            return false;
        }
        return true;
    }

    /// <summary>Seconds left until the cached PIN expires, without decrypting it. Returns <see langword="null"/> when
    /// no PIN is cached, and <see cref="int.MaxValue"/> when the TTL is 0 (kept until the program exits).</summary>
    public static int? remainingSeconds() {
        if (encryptedPin is null) {
            return null;
        }
        if (ttlSeconds <= 0) {
            return int.MaxValue;
        }
        return Math.Max(0, ttlSeconds - (int) Math.Ceiling((Environment.TickCount64 - cachedAtMs) / 1000.0));
    }

    /// <summary>Encrypts <paramref name="pin"/> in memory. Returns <see langword="false"/> if a debugger is attached
    /// (the cache then stays empty) or if the OS encryption failed.</summary>
    public static bool set(string pin) {
        if (debuggerAttached()) {
            LOGGER.Warn("Refusing to cache the security key PIN because a debugger is attached");
            return false;
        }
        clear();
        byte[]? buffer = null;
        try {
            byte[] plain  = Encoding.UTF8.GetBytes(pin);
            uint   size   = (uint) ((plain.Length + BLOCK_SIZE - 1) / BLOCK_SIZE * BLOCK_SIZE);
            buffer = new byte[size];
            plain.CopyTo(buffer, 0);
            Array.Clear(plain); // drop the plaintext copy immediately

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try {
                if (!CryptProtectMemory(handle.AddrOfPinnedObject(), size, CRYPTPROTECTMEMORY_SAME_PROCESS)) {
                    return false;
                }
            } finally {
                handle.Free();
            }

            encryptedPin = buffer;
            buffer       = null; // ownership moved to the cache; clear() zeroes it from now on
            cachedAtMs   = Environment.TickCount64;
            return true;
        } catch (Exception) {
            return false;
        } finally {
            if (buffer is not null) {
                Array.Clear(buffer); // an exception left plaintext behind; zero it before the GC moves it around
            }
        }
    }

    /// <summary>Forgets the cached PIN and zeroes its memory immediately.</summary>
    public static void clear() {
        if (encryptedPin is not null) {
            Array.Clear(encryptedPin);
            encryptedPin = null;
        }
    }

    /// <summary>Whether any debugger — our own process's or one attached from outside — is currently attached.</summary>
    private static bool debuggerAttached() =>
        IsDebuggerPresent() || (CheckRemoteDebuggerPresent(GetCurrentProcess(), out bool remote) && remote);

    /// <summary>Whether the cached PIN has outlived its TTL. TTL 0 means "kept until the program exits" and never
    /// expires.</summary>
    private static bool isExpired() => ttlSeconds > 0 && Environment.TickCount64 - cachedAtMs > ttlSeconds * 1000L;

    /// <summary>Decrypts a copy of the cached bytes and returns the temporary plaintext buffer (still padded to the
    /// encryption block size); the caller zeroes it. Returns <see langword="null"/> if the OS decryption failed.</summary>
    private static byte[]? decrypt(byte[] encrypted) {
        var    buffer = (byte[]) encrypted.Clone();
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try {
            if (!CryptUnprotectMemory(handle.AddrOfPinnedObject(), (uint) buffer.Length, CRYPTPROTECTMEMORY_SAME_PROCESS)) {
                Array.Clear(buffer);
                return null;
            }
            return buffer;
        } finally {
            handle.Free();
        }
    }

}
