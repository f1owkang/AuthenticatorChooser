using System.Runtime.InteropServices;
using System.Text;

namespace AuthenticatorChooser.Windows11;

/// <summary>
/// Verifies that a window's owning process is a trusted Microsoft system binary before the program interacts with it.
/// This closes the phishing hole where a same-user (or lower-integrity) process could register the
/// "Credential Dialog Xaml Host" window class, fake the UIA shape, and steal the cached FIDO PIN.
/// </summary>
internal static class WindowTrust {

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE          = 2;
    private const uint WTD_REVOKE_NONE      = 0;
    private const uint WTD_CHOICE_FILE      = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_SAFER_FLAG       = 0x100;

    /// <summary>System processes that legitimately own a "Credential Dialog Xaml Host" window.</summary>
    private static readonly HashSet<string> TRUSTED_PROCESSES = new(StringComparer.OrdinalIgnoreCase) {
        "CredentialUIBroker.exe", // Windows Security FIDO/WebAuthn dialogs
        "Consent.exe",            // UAC elevation prompt shares the same XAML host window class
        "LogonUI.exe",            // lock/logon credential UI
        "winlogon.exe"            // credential provider host
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO {
        public uint   cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA {
        public uint   cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint   dwUIChoice;
        public uint   fdwRevocationChecks;
        public uint   dwUnionChoice;
        public IntPtr pFile;
        public uint   dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint   dwProvFlags;
        public uint   dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    /// <summary>Whether the window belongs to a Microsoft-signed Windows system process allowed to own FIDO dialogs.</summary>
    public static bool isTrustedSystemProcess(IntPtr hwnd) {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) {
            return false;
        }

        string? path = getProcessPath(pid);
        if (path is null) {
            return false;
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.Equals(directory, Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase)
            || !TRUSTED_PROCESSES.Contains(Path.GetFileName(path))) {
            return false;
        }

        return isMicrosoftSigned(path);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>Resolves a PID to its executable path using only limited-query access, which works across integrity
    /// levels (CredentialUIBroker runs at System IL) and on protected processes, unlike <c>Process.MainModule</c>.</summary>
    private static string? getProcessPath(uint pid) {
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) {
            return null;
        }
        try {
            var  fileName = new StringBuilder(32768);
            uint size     = (uint) fileName.Capacity;
            return QueryFullProcessImageNameW(hProcess, 0, fileName, ref size) ? fileName.ToString() : null;
        } finally {
            CloseHandle(hProcess);
        }
    }

    private static bool isMicrosoftSigned(string path) {
        IntPtr pFile = IntPtr.Zero;
        try {
            WINTRUST_FILE_INFO fileInfo = new() {
                cbStruct       = (uint) Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath  = path,
                hFile          = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, pFile, false);

            WINTRUST_DATA data = new() {
                cbStruct            = (uint) Marshal.SizeOf<WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData      = IntPtr.Zero,
                dwUIChoice          = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice       = WTD_CHOICE_FILE,
                pFile               = pFile,
                dwStateAction       = WTD_STATEACTION_IGNORE,
                hWVTStateData       = IntPtr.Zero,
                pwszURLReference    = IntPtr.Zero,
                dwProvFlags         = WTD_SAFER_FLAG,
                dwUIContext         = 0,
                pSignatureSettings  = IntPtr.Zero
            };

            Guid action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        } catch (Exception) {
            return false;
        } finally {
            if (pFile != IntPtr.Zero) {
                Marshal.FreeHGlobal(pFile);
            }
        }
    }

}
