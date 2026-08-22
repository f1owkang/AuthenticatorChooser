using AuthenticatorChooser.Resources;
using System.Globalization;

namespace AuthenticatorChooser;

/// <summary>
/// <para>Localization of the program's own UI (the system tray context menu), independent from the FIDO dialog
/// matching strings in <see cref="I18N"/>, which must follow the Windows UI language (issue #4).</para>
/// <para>The tray menu language is switchable at runtime via the "Language" submenu, and defaults to following the
/// system UI language.</para>
/// </summary>
public static class UiLanguage {

    /// <summary>The UI languages that have translated strings in <c>LocalizedStrings.*.resx</c>, shown in the tray menu's
    /// "Language" submenu. The first entry (English) is the neutral fallback when an unsupported system language is used.</summary>
    public static readonly IReadOnlyList<(string name, string displayName)> SUPPORTED = [
        ("en", "English"),
        ("zh-CN", "简体中文"),
        ("zh-TW", "繁體中文")
    ];

    /// <summary><see langword="null"/> means "follow the system UI language".</summary>
    private static string? selectedCulture;

    /// <summary>Gets a user-visible string for the current UI culture, falling back to English if untranslated.</summary>
    public static string get(string key) =>
        LocalizedStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>
    /// Applies a UI language for this process. Pass <see langword="null"/> to follow the system UI language.
    /// Only <see cref="CultureInfo.CurrentUICulture"/> is changed, because FIDO dialog matching must keep following
    /// the Windows UI language.
    /// </summary>
    public static void apply(string? cultureName) {
        selectedCulture = cultureName;
        CultureInfo.CurrentUICulture = cultureName is null ? CultureInfo.InstalledUICulture : CultureInfo.GetCultureInfo(cultureName);
    }

    /// <summary>The currently selected UI culture name, or <see langword="null"/> when following the system language.</summary>
    public static string? currentCultureName() => selectedCulture;

}
