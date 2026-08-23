using Microsoft.Win32;
using System.Security.Principal;

namespace PasskeyPick;

/// <summary>
/// <para>Enumerates the authentication methods currently registered on this Windows system, so the tray context menu
/// can list them without any manual configuration (issue #5).</para>
/// <para>Third-party passkey providers (1Password, Bitwarden, KeePass, ...) register as FIDO plugins under
/// <c>HKLM\SOFTWARE\Microsoft\FIDO\&lt;user SID&gt;\Plugins\&lt;plugin GUID&gt;</c>, where a <c>Name</c> value holds the
/// display name shown in the "Choose a passkey" dialog.</para>
/// </summary>
public static class PasskeyProviders {

    /// <summary>Lists the display names of all registered third-party passkey providers. Never throws, because the program must keep running.</summary>
    public static IReadOnlyList<string> enumerate() {
        try {
            string? userSid = WindowsIdentity.GetCurrent().User?.Value;
            if (userSid is null) {
                return [];
            }

            using RegistryKey? plugins = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\FIDO\{userSid}\Plugins");
            if (plugins is null) {
                return [];
            }

            var names = new List<string>();
            foreach (string pluginGuid in plugins.GetSubKeyNames()) {
                using RegistryKey? plugin = plugins.OpenSubKey(pluginGuid);
                // Windows writes the provider display name as "Name"; accept a couple of common alternates for robustness
                string? displayName = plugin?.GetValue("Name") as string ?? plugin?.GetValue("DisplayName") as string;
                if (!string.IsNullOrWhiteSpace(displayName)) {
                    names.Add(displayName.Trim());
                }
            }
            return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        } catch (Exception) {
            // An unreadable registry must not crash the program
            return [];
        }
    }

}
