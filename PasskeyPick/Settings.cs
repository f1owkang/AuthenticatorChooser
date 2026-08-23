using System.Text.Json;

namespace PasskeyPick;

/// <summary>
/// Persists user preferences in the per-user %APPDATA% directory so they survive restarts: the UI language, the
/// automatic-selection and PIN-cache settings, and the preferred authenticator. The PIN itself is never persisted.
/// </summary>
internal static class Settings {

    public static readonly string PATH = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Startup.PROGRAM_NAME, "settings.json");

    /// <summary>The UI language selected in the tray menu, or <see langword="null"/> to follow the system language.</summary>
    public static string? uiLanguage { get; set; }

    /// <summary>Whether the program automatically chooses the security key in FIDO prompts.</summary>
    public static bool autoSelectEnabled { get; set; } = true;

    /// <summary>The display name of the authenticator preferred from the tray menu, or <see langword="null"/> for automatic.</summary>
    public static string? preferredAuthenticator { get; set; }

    /// <summary>The auto-submit PIN length, or <see langword="null"/> to not auto-submit PIN prompts.</summary>
    public static int? autoSubmitPinLength { get; set; }

    /// <summary>Cache TTL in seconds, where 0 keeps the PIN until the program exits.</summary>
    public static int pinCacheTtlSeconds { get; set; } = 600;

    public static bool pinClearOnLock { get; set; }
    public static bool pinClearOnSleep { get; set; }
    public static bool pinClearOnHibernate { get; set; }

    /// <summary>Whether the gpg-agent TCP bridge (remote Git signing) is enabled.</summary>
    public static bool gpgBridgeEnabled { get; set; }

    /// <summary>Whether PasskeyPick starts and keeps the Gpg4win gpg-agent alive.</summary>
    public static bool gpgAgentAutostartEnabled { get; set; }

    /// <summary>Local port the gpg-agent TCP bridge listens on (loopback only).</summary>
    public static int gpgBridgePort { get; set; } = 4321;

    /// <summary>UTC time of the last update check, so the GitHub API is hit at most once a day.</summary>
    public static DateTime? lastUpdateCheckUtc { get; set; }

    /// <summary>Serializable snapshot of <see cref="Settings"/>, so a static class can be persisted.</summary>
    private sealed class Dto {
        public string? uiLanguage { get; init; }
        public bool autoSelectEnabled { get; init; } = true;
        public string? preferredAuthenticator { get; init; }
        public int? autoSubmitPinLength { get; init; }
        public int pinCacheTtlSeconds { get; init; } = 600;
        public bool pinClearOnLock { get; init; }
        public bool pinClearOnSleep { get; init; }
        public bool pinClearOnHibernate { get; init; }
        public bool gpgBridgeEnabled { get; init; }
        public bool gpgAgentAutostartEnabled { get; init; }
        public int gpgBridgePort { get; init; } = 4321;
        public DateTime? lastUpdateCheckUtc { get; init; }
    }

    public static void load() {
        try {
            if (File.Exists(PATH) && JsonSerializer.Deserialize<Dto>(File.ReadAllText(PATH)) is { } dto) {
                uiLanguage            = dto.uiLanguage;
                autoSelectEnabled     = dto.autoSelectEnabled;
                preferredAuthenticator = dto.preferredAuthenticator;
                autoSubmitPinLength   = dto.autoSubmitPinLength;
                pinCacheTtlSeconds    = dto.pinCacheTtlSeconds;
                pinClearOnLock        = dto.pinClearOnLock;
                pinClearOnSleep       = dto.pinClearOnSleep;
                pinClearOnHibernate   = dto.pinClearOnHibernate;
                gpgBridgeEnabled         = dto.gpgBridgeEnabled;
                gpgAgentAutostartEnabled = dto.gpgAgentAutostartEnabled;
                gpgBridgePort            = dto.gpgBridgePort;
                lastUpdateCheckUtc    = dto.lastUpdateCheckUtc;
            }
        } catch (Exception) {
            // A corrupt settings file must not crash the program; fall back to defaults.
        }
    }

    public static void save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(PATH)!);
            File.WriteAllText(PATH, JsonSerializer.Serialize(new Dto {
                uiLanguage            = uiLanguage,
                autoSelectEnabled     = autoSelectEnabled,
                preferredAuthenticator = preferredAuthenticator,
                autoSubmitPinLength   = autoSubmitPinLength,
                pinCacheTtlSeconds    = pinCacheTtlSeconds,
                pinClearOnLock        = pinClearOnLock,
                pinClearOnSleep       = pinClearOnSleep,
                pinClearOnHibernate   = pinClearOnHibernate,
                gpgBridgeEnabled         = gpgBridgeEnabled,
                gpgAgentAutostartEnabled = gpgAgentAutostartEnabled,
                gpgBridgePort            = gpgBridgePort,
                lastUpdateCheckUtc    = lastUpdateCheckUtc
            }));
        } catch (Exception) {
            // Failing to save settings must not crash the program.
        }
    }

}
