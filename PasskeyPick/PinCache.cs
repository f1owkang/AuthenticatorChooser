using System.Runtime.InteropServices;
using System.Text;

namespace PasskeyPick;

/// <summary>
/// Holds the USB security key's FIDO2 PIN in memory only — never written to disk — with a configurable TTL, modeled on
/// gpg-agent's passphrase cache (<c>default-cache-ttl</c>). The cached bytes are encrypted in place with
/// <c>CryptProtectMemory</c> (a per-process DPAPI session key), so the PIN is never stored in plaintext; on use the
/// bytes are decrypted into a temporary buffer (zeroed immediately) and then materialized into a managed string for the
/// UIA <c>ValuePattern</c> call — a managed string cannot be zeroed, so a brief plaintext copy necessarily exists on
/// the GC heap for the duration of that call. The PIN must be re-entered after every program restart.
/// </summary>
internal static class PinCache {

    private const uint CRYPTPROTECTMEMORY_SAME_PROCESS = 0x00;
    private const uint BLOCK_SIZE = 16; // CRYPTPROTECTMEMORY_BLOCK_SIZE

    /// <summary>How long a cached PIN stays valid, mirroring gpg-agent's <c>default-cache-ttl</c> (600 s). Zero keeps it
    /// until the program exits (a restart), rather than expiring on a timer.</summary>
    private static int ttlSeconds = 600;

    /// <summary>Whether the PIN must be forgotten when Windows locks, sleeps, or hibernates.</summary>
    private static bool clearOnLock;
    private static bool clearOnSleep;
    private static bool clearOnHibernate;

    /// <summary>The PIN encrypted with <see cref="CryptProtectMemory"/>; never holds plaintext.</summary>
    private static byte[]? encryptedPin;
    private static DateTime cachedAt;

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectMemory(IntPtr pDataIn, uint cbDataIn, uint dwFlags);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectMemory(IntPtr pDataIn, uint cbDataIn, uint dwFlags);

    /// <summary>Applies the persisted preferences. Call once at startup, before any FIDO dialogs may appear.</summary>
    public static void initialize() {
        ttlSeconds       = Settings.pinCacheTtlSeconds;
        clearOnLock      = Settings.pinClearOnLock;
        clearOnSleep     = Settings.pinClearOnSleep;
        clearOnHibernate = Settings.pinClearOnHibernate;
    }

    /// <summary>The cache TTL in seconds, where 0 keeps the PIN until the program exits. Persisted on change.</summary>
    public static int ttlSecondsValue {
        get => ttlSeconds;
        set {
            ttlSeconds = value;
            Settings.pinCacheTtlSeconds = value;
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

    /// <summary>Returns the cached PIN while it is within its TTL, otherwise clears it and returns <see langword="null"/>.</summary>
    public static string? tryGetCached() {
        if (encryptedPin is null) {
            return null;
        }
        if (ttlSeconds > 0 && DateTime.Now - cachedAt > TimeSpan.FromSeconds(ttlSeconds)) {
            clear();
            return null;
        }
        return decrypt(encryptedPin);
    }

    /// <summary>Whether a PIN is currently cached and still within its TTL. Checks only the timestamp and never
    /// decrypts, so opening the tray menu does not materialize a plaintext copy of the PIN.</summary>
    public static bool hasCached() {
        if (encryptedPin is null) {
            return false;
        }
        if (ttlSeconds > 0 && DateTime.Now - cachedAt > TimeSpan.FromSeconds(ttlSeconds)) {
            clear();
            return false;
        }
        return true;
    }

    /// <summary>Seconds left until the cached PIN expires, without decrypting it. Returns <see langword="null"/> when no
    /// PIN is cached, and <see cref="int.MaxValue"/> when the TTL is 0 (kept until the program exits).</summary>
    public static int? remainingSeconds() {
        if (encryptedPin is null) {
            return null;
        }
        if (ttlSeconds <= 0) {
            return int.MaxValue;
        }
        return Math.Max(0, ttlSeconds - (int) Math.Ceiling((DateTime.Now - cachedAt).TotalSeconds));
    }

    /// <summary>Encrypts <paramref name="pin"/> in memory. Returns <see langword="false"/> if the OS encryption failed.</summary>
    public static bool set(string pin) {
        clear();
        try {
            byte[] plain  = Encoding.UTF8.GetBytes(pin);
            uint   size   = (uint) ((plain.Length + BLOCK_SIZE - 1) / BLOCK_SIZE * BLOCK_SIZE);
            var    buffer = new byte[size];
            plain.CopyTo(buffer, 0);
            Array.Clear(plain); // drop the plaintext copy immediately

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try {
                if (!CryptProtectMemory(handle.AddrOfPinnedObject(), size, CRYPTPROTECTMEMORY_SAME_PROCESS)) {
                    Array.Clear(buffer);
                    return false;
                }
            } finally {
                handle.Free();
            }

            encryptedPin = buffer;
            cachedAt     = DateTime.Now;
            return true;
        } catch (Exception) {
            return false;
        }
    }

    /// <summary>Forgets the cached PIN and zeroes its memory immediately.</summary>
    public static void clear() {
        if (encryptedPin is not null) {
            Array.Clear(encryptedPin);
            encryptedPin = null;
        }
    }

    /// <summary>Decrypts a copy of the cached bytes to a temporary buffer and zeroes it. The plaintext is then
    /// unavoidable as a managed string for the UIA <c>ValuePattern</c> call; only the byte buffer can be zeroed.</summary>
    private static string? decrypt(byte[] encrypted) {
        var    buffer = (byte[]) encrypted.Clone();
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try {
            if (!CryptUnprotectMemory(handle.AddrOfPinnedObject(), (uint) buffer.Length, CRYPTPROTECTMEMORY_SAME_PROCESS)) {
                return null;
            }
            // Padding bytes are zero; UTF-8 never contains NUL, so the first zero is the real end of the PIN.
            int length = Array.IndexOf(buffer, (byte) 0);
            if (length < 0) {
                length = buffer.Length;
            }
            return Encoding.UTF8.GetString(buffer, 0, length);
        } finally {
            Array.Clear(buffer);
            handle.Free();
        }
    }

}
