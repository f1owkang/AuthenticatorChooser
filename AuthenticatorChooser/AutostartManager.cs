using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System.Security.Principal;

namespace AuthenticatorChooser;

/// <summary>
/// Registers, checks, and unregisters the program's logon autostart scheduled task, shared by the
/// <c>--autostart-on-logon</c> command-line argument and the system tray menu toggle. Modeled on g-helper: the task
/// name is per-user (SID), and it only runs elevated when the app itself is running elevated.
/// </summary>
public static class AutostartManager {

    /// <summary>Name of the scheduled task used to start the program at logon, unique per user.</summary>
    public static string taskName => $"{Startup.PROGRAM_NAME}_{WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName}";

    /// <summary>Whether the logon scheduled task currently exists.</summary>
    public static bool isEnabled() {
        try {
            return TaskService.Instance.GetTask(taskName) != null;
        } catch (Exception) {
            return false;
        }
    }

    /// <summary>Creates (or updates) the logon scheduled task. Returns <see langword="false"/> on failure so the caller can surface the error.</summary>
    public static bool enable(ChooserOptions options) {
        try {
            string domainAndUsername = $@"{Environment.UserDomainName}\{Environment.UserName}";
            // g-helper: only request elevation when the app is already elevated; a normal user cannot register a
            // Highest-privilege task, so the toggle used to fail silently.
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

            TaskDefinition scheduledTask = TaskService.Instance.NewTask();
            scheduledTask.RegistrationInfo.Author      = "Ben Hutchison";
            scheduledTask.RegistrationInfo.Date        = DateTime.Now;
            scheduledTask.RegistrationInfo.Description =
                $"{Startup.PROGRAM_NAME} is a background program that skips the phone pairing option and chooses the USB security key in Windows FIDO/WebAuthn prompts. \n\nThis scheduled task is necessary to start {Startup.PROGRAM_NAME} for you on login with elevated permissions, which are required to interact with the Windows 11 FIDO prompts beginning in January 2026. \n\nhttps://github.com/Aldaviva/{Startup.PROGRAM_NAME}";
            if (isAdmin) {
                scheduledTask.Principal.RunLevel = TaskRunLevel.Highest; // #44: CredentialUIBroker runs with UIAccess integrity level
            }
            scheduledTask.Settings.Enabled                    = true;
            scheduledTask.Settings.ExecutionTimeLimit         = TimeSpan.Zero;
            scheduledTask.Settings.DisallowStartIfOnBatteries = false;
            scheduledTask.Settings.StopIfGoingOnBatteries     = false;
            scheduledTask.Settings.Compatibility              = TaskCompatibility.V2_3;
            scheduledTask.Actions.Add(Environment.ProcessPath!, buildStartupArguments(options));
            scheduledTask.Triggers.Add(new LogonTrigger { Enabled = true, UserId = domainAndUsername, Delay = TimeSpan.FromSeconds(3) });
            TaskService.Instance.RootFolder.RegisterTaskDefinition(taskName, scheduledTask, TaskCreation.CreateOrUpdate, domainAndUsername, null, TaskLogonType.InteractiveToken);

            // #44: Remove the old 0.4.0 registry startup entry, which is no longer adequate
            using RegistryKey? userRun = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (userRun is not null) {
                try {
                    userRun.DeleteValue(Startup.PROGRAM_NAME, true);
                } catch (ArgumentException) {
                    // value had already been removed
                }
            }
            return true;
        } catch (Exception) {
            return false;
        }
    }

    /// <summary>Removes the logon scheduled task, if present.</summary>
    public static void disable() {
        try {
            TaskService.Instance.RootFolder.DeleteTask(taskName, false);
        } catch (Exception) {
            // task was not registered; nothing to remove
        }
        // Also remove the legacy task name (display-name based) from before the SID-based rename.
        try {
            TaskService.Instance.RootFolder.DeleteTask($"{Startup.PROGRAM_NAME} \u2013 {Environment.UserName}", false);
        } catch (Exception) {
            // no legacy task; nothing to remove
        }
    }

    /// <summary>Builds the command-line arguments to persist in the scheduled task, restoring the same behavior after a reboot.</summary>
    private static string? buildStartupArguments(ChooserOptions options) {
        List<string> args = [];
        if (options.skipAllNonSecurityKeyOptions) {
            args.Add("--skip-all-non-security-key-options");
        }
        if (options.priorityFile is { Length: > 0 }) {
            args.Add($"--priority-file=\"{options.priorityFile}\"");
        }
        if (options.autoSubmitPinLength is { } pinLength) {
            args.Add($"--autosubmit-pin-length={pinLength}");
        }
        return args.Count == 0 ? null : string.Join(' ', args);
    }

}
