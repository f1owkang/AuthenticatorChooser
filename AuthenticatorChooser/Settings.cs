using System.Text.Json;

namespace AuthenticatorChooser;

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
                pinClearOnHibernate   = pinClearOnHibernate
            }));
        } catch (Exception) {
            // Failing to save settings must not crash the program.
        }
    }

}
