using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;

namespace PasskeyPick;

/// <summary>
/// Deployment-environment hardening for two findings: DLL search-order hijacking (the executable directory must not be
/// writable by unprivileged users, or a malicious library could be placed next to the executable and loaded) and
/// priority.txt poisoning (the config file must be owned by the current user or a local administrator, or it may have
/// been planted).
/// </summary>
internal static class Security {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(Security).FullName!);

    private static readonly SecurityIdentifier CURRENT_USER_SID;

    static Security() {
        using WindowsIdentity current = WindowsIdentity.GetCurrent();
        CURRENT_USER_SID = current.User!;
    }

    /// <summary>SIDs that must never be able to write to the executable's directory.</summary>
    private static readonly WellKnownSidType[] UNSAFE_DIRECTORY_SIDS = [
        WellKnownSidType.WorldSid,             // Everyone
        WellKnownSidType.AuthenticatedUserSid, // Authenticated Users
        WellKnownSidType.BuiltinUsersSid,      // Users
        WellKnownSidType.BuiltinGuestsSid      // Guests
    ];

    private const FileSystemRights WRITE_RIGHTS =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.CreateFiles |
        FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>
    /// Warns if the executable's directory grants write access to unprivileged principals, which would let a
    /// lower-privileged user plant a malicious DLL or overwrite priority.txt. Does not block startup: a false positive
    /// (or a deliberate deployment choice) must not prevent the program from working. Because the app is a WinExe with
    /// no console, the warning is surfaced as a tray balloon (and logged), not just written to the log file.
    /// </summary>
    public static void warnIfDeploymentDirectoryIsInsecure() {
        try {
            string           path = AppContext.BaseDirectory;
            DirectorySecurity acl = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
            if (isWritableByUnsafePrincipal(acl)) {
                LOGGER.Error("The program directory {dir} grants write access to unprivileged users, allowing a lower-privileged user to plant a malicious DLL (DLL search-order hijacking) or overwrite priority.txt. Move the executable to a protected directory such as C:\\Program Files\\PasskeyPick.", path);
                TrayNotifications.show("deploymentDirInsecureTitle", "deploymentDirInsecureBody", ToolTipIcon.Warning, path);
            }
        } catch (Exception e) {
            LOGGER.Warn("Could not check the program directory ACL ({message})", e.Message);
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> is owned by the current user or a local administrator/system account. A
    /// priority.txt owned by anyone else may have been planted by a lower-privileged user, so it must not be trusted.
    /// Returns <see langword="false"/> when ownership cannot be read — an unverifiable owner is not trusted
    /// (fail closed), so a config file that cannot be vetted is simply ignored by its caller.
    /// </summary>
    public static bool isOwnedByTrustedPrincipal(string path) {
        try {
            if (new FileInfo(path).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner) {
                return false;
            }
            return owner == CURRENT_USER_SID
                || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
                || owner.IsWellKnown(WellKnownSidType.LocalSystemSid)
                || owner.IsWellKnown(WellKnownSidType.LocalServiceSid);
        } catch (Exception) {
            return false; // fail closed: unknown ownership must not be trusted
        }
    }

    private static bool isWritableByUnsafePrincipal(DirectorySecurity acl) {
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier))) {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & WRITE_RIGHTS) == 0) {
                continue;
            }
            var sid = (SecurityIdentifier) rule.IdentityReference;
            foreach (WellKnownSidType unsafeSid in UNSAFE_DIRECTORY_SIDS) {
                if (sid.IsWellKnown(unsafeSid)) {
                    return true;
                }
            }
        }
        return false;
    }

}
