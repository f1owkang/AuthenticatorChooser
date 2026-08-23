using Microsoft.Win32;

namespace PasskeyPick.Windows11;

/// <param name="name">Microsoft Windows 11 Pro</param>
/// <param name="marketingVersion">24H2</param>
/// <param name="version">10.0.26100.3775 (major version is 10 on Windows 11)</param>
/// <param name="architecture">AMD64</param>
internal readonly record struct OsVersion(string name, string marketingVersion, Version version, string architecture) {

    private const string NT_CURRENTVERSION_KEY = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    /// <summary>
    /// Reads OS version info without WMI, because <see cref="System.Management"/>'s WMI query can throw
    /// <c>ManagementException: Critical error</c> on some systems (issue #40), crashing the program at startup.
    /// All data comes from the registry and environment variables, which are always available.
    /// </summary>
    public static OsVersion getCurrent() {
        // #40: product name like "Windows 11 Pro"; fall back gracefully if any registry read fails
        string name = readRegistryString(NT_CURRENTVERSION_KEY, "ProductName") is { } productName && !string.IsNullOrWhiteSpace(productName)
            ? productName
            : "Microsoft Windows";

        string marketingVersion = readRegistryString(NT_CURRENTVERSION_KEY, "DisplayVersion") ?? string.Empty;

        int currentBuild     = readRegistryInt(NT_CURRENTVERSION_KEY, "CurrentBuildNumber", 0);
        int ubr              = readRegistryInt(NT_CURRENTVERSION_KEY, "UBR", 0);
        int major            = readRegistryInt(NT_CURRENTVERSION_KEY, "CurrentMajorVersionNumber", 10);
        int minor            = readRegistryInt(NT_CURRENTVERSION_KEY, "CurrentMinorVersionNumber", 0);
        // If both fallbacks fail, fall back to Environment.OSVersion for a best-effort major.minor
        if (major == 0 && Environment.OSVersion.Version.Major > 0) {
            major = Environment.OSVersion.Version.Major;
            minor = Environment.OSVersion.Version.Minor;
        }

        Version version = currentBuild > 0 ? new Version(major, minor, currentBuild, ubr) : Environment.OSVersion.Version;

        string architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty;

        return new OsVersion(name, marketingVersion, version, architecture);
    }

    private static string? readRegistryString(string keyPath, string valueName) {
        try {
            return Registry.GetValue(keyPath, valueName, null) as string;
        } catch (Exception) {
            // #40: registry reads must never crash startup
            return null;
        }
    }

    private static int readRegistryInt(string keyPath, string valueName, int defaultValue) {
        try {
            object? rawValue = Registry.GetValue(keyPath, valueName, null);
            return rawValue switch {
                int    i => i,
                string s when int.TryParse(s, out int parsed) => parsed,
                _        => defaultValue
            };
        } catch (Exception) {
            // #40: registry reads must never crash startup
            return defaultValue;
        }
    }

}