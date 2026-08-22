using AuthenticatorChooser.Windows11;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Conventions;
using Microsoft.Win32;
using System.Reflection;
using System.Security.Principal;

// ReSharper disable ClassNeverInstantiated.Global - it's actually instantiated by McMaster.Extensions.CommandLineUtils
// ReSharper disable UnassignedGetOnlyAutoProperty - it's actually assigned by McMaster.Extensions.CommandLineUtils

namespace AuthenticatorChooser;

public class Startup {

    internal const string PROGRAM_NAME = nameof(AuthenticatorChooser);

    internal static readonly string                  PROGRAM_VERSION = Assembly.GetEntryAssembly()!.GetName().Version!.ToString(3);
    private static readonly CancellationTokenSource  EXITING_TRIGGER = new();
    public static readonly  CancellationToken        EXITING         = EXITING_TRIGGER.Token;
    private static readonly WindowsIdentity          CURRENT_USER    = WindowsIdentity.GetCurrent();

    private static Logger? logger;

    // #15
    [Option("--skip-all-non-security-key-options", CommandOptionType.NoValue)]
    public bool skipAllNonSecurityKeyOptions { get; }

    // #63: a config file that ranks authenticator options (like third-party passkey providers) by priority
    [Option("--priority-file", CommandOptionType.SingleValue)]
    public string? priorityFile { get; }

    // #30
    [Option("--autosubmit-pin-length", CommandOptionType.SingleValue)]
    public int? autosubmitPinLength { get; }

    [Option("--autostart-on-logon", CommandOptionType.NoValue)]
    public bool autostartOnLogon { get; }

    [Option("--set-pin", CommandOptionType.NoValue)]
    public bool setPin { get; }

    [Option("--pin-cache-ttl", CommandOptionType.SingleValue)]
    public int? pinCacheTtlSeconds { get; }

    [Option("--pin-clear-on-lock", CommandOptionType.NoValue)]
    public bool pinClearOnLock { get; }

    [Option("--pin-clear-on-sleep", CommandOptionType.NoValue)]
    public bool pinClearOnSleep { get; }

    [Option("--pin-clear-on-hibernate", CommandOptionType.NoValue)]
    public bool pinClearOnHibernate { get; }

    [Option("-l|--log", CommandOptionType.SingleOrNoValue)]
    public (bool enabled, string? filename) log { get; }

    [Option(DefaultHelpOptionConvention.DefaultHelpTemplate, CommandOptionType.NoValue)]
    public bool help { get; }

    // ReSharper disable once UnusedMember.Global - it's actually invoked by McMaster.Extensions.CommandLineUtils
    // ReSharper disable once InconsistentNaming - it must be named this, as dictated by McMaster.Extensions.CommandLineUtils, it's not my choice
    public int OnExecute() {
        Logging.initialize(log.enabled, log.filename);
        logger = LogManager.GetLogger(typeof(Startup).FullName!);

        try {
            if (help) {
                showUsage();
                return 0;
            }

            if (autostartOnLogon && !registerAsStartupProgram()) {
                return 1;
            }

            // Load persisted preferences (UI language, auto-select, preferred authenticator, PIN cache); explicit
            // command-line arguments take precedence. The PIN itself is never persisted.
            Settings.load();
            if (pinCacheTtlSeconds is { } ttlSeconds) {
                Settings.pinCacheTtlSeconds = ttlSeconds;
            }
            Settings.pinClearOnLock      |= pinClearOnLock;
            Settings.pinClearOnSleep     |= pinClearOnSleep;
            Settings.pinClearOnHibernate |= pinClearOnHibernate;
            if (autosubmitPinLength is { } pinLength) {
                Settings.autoSubmitPinLength = pinLength;
            }
            PinCache.initialize();
            if (setPin) {
                Program.setupPin();
            }

            using Mutex singleInstanceLock = new(true, $@"Local\{PROGRAM_NAME}_{CURRENT_USER.User?.Value}", out bool isOnlyInstance);
            CURRENT_USER.Dispose();
            if (!isOnlyInstance) {
                logger.Warn("Another instance of {program} is already running for this user, this instance is exiting now.", PROGRAM_NAME);
                return 2;
            }

            try {
                logger.Info("{name} {version} starting", PROGRAM_NAME, PROGRAM_VERSION);
                OsVersion os = OsVersion.getCurrent();
                logger.Info("Operating system is {name} {marketingVersion} {version} {arch}", os.name, os.marketingVersion, os.version, os.architecture);
                logger.Info("{Locales are} {locales}", I18N.LOCALE_NAMES.Count == 1 ? "Locale is" : "Locales are", string.Join(", ", I18N.LOCALE_NAMES));
                logger.Info("Waiting for Windows Security FIDO dialog boxes to open");

                Security.warnIfDeploymentDirectoryIsInsecure();

                UiLanguage.apply(Settings.uiLanguage);

                ChooserOptions options = new(skipAllNonSecurityKeyOptions, Settings.autoSubmitPinLength, resolvePriorityFile(priorityFile));
                options.isEnabled = Settings.autoSelectEnabled;
                options.preferredAuthenticator = Settings.preferredAuthenticator;

                Console.CancelKeyPress += (_, args) => {
                    args.Cancel = true;
                    requestExit();
                };

                SystemEvents.SessionEnding += onWindowsLogoff;
                SystemEvents.SessionSwitch += onSessionSwitch;
                SystemEvents.PowerModeChanged += onPowerModeChanged;
                // Last-resort cleanup so a cached PIN is zeroed on any orderly (or most abnormal) process exit.
                AppDomain.CurrentDomain.ProcessExit += (_, _) => PinCache.clear();

                // Blocks on the WinForms message loop until the app exits
                Program.launch(options);
            } finally {
                singleInstanceLock.ReleaseMutex();
            }

            return 0;
        } catch (Exception e) when (e is not OutOfMemoryException) {
            logger.Error(e, "Uncaught exception");
            Win32MessageBox.show($"Uncaught exception: {e}", PROGRAM_NAME, Win32MessageBox.Kind.Error);
            return 1;
        } finally {
            LogManager.Shutdown();
            CURRENT_USER.Dispose();
        }
    }

    /// <summary>Cancels the shared exit token so background waits stop. The WinForms app is closed by the tray menu's
    /// exit handler, which calls <see cref="Program.launch"/>'s <see cref="TrayApplicationContext"/>.</summary>
    internal static void requestExit() => EXITING_TRIGGER.Cancel();

    private bool registerAsStartupProgram() {
        if (AutostartManager.enable(new ChooserOptions(skipAllNonSecurityKeyOptions, autosubmitPinLength, resolvePriorityFile(priorityFile)))) {
            Win32MessageBox.show($"{PROGRAM_NAME} is now running in the background, and will also start automatically each time you log in to Windows.", PROGRAM_NAME, Win32MessageBox.Kind.Information);
            return true;
        }
        Win32MessageBox.show($"Failed to register {PROGRAM_NAME} to start automatically on Windows logon.", PROGRAM_NAME, Win32MessageBox.Kind.Error);
        return false;
    }

    private static string? resolvePriorityFile(string? configuredPath) {
        if (configuredPath is { Length: > 0 }) {
            return configuredPath;
        }
        // #63: if the user didn't pass --priority-file, only use priority.txt next to the executable when it actually
        // exists. Otherwise there is no priority list, which keeps the conservative default of not auto-submitting when
        // other valid authenticator options are present.
        string defaultPath = Path.Combine(AppContext.BaseDirectory, PriorityChooser.DEFAULT_FILENAME);
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    private static void showUsage() {
        string processFilename = Path.GetFileName(Environment.ProcessPath)!;
        Win32MessageBox.show(
            $"""
            {processFilename}
                Runs this program in the background, waiting for FIDO credential dialog boxes to open and choosing the Security Key option each time.
              
            {processFilename} --autostart-on-logon
                Registers this program to start automatically every time the current user logs on to Windows, and also leaves it running in the background like the first example.
                
            {processFilename} --skip-all-non-security-key-options
                Forces this program to choose the Security Key option even if there are other valid options, such as an already-paired phone or Windows Hello PIN or biometrics. By default, without this option, it will only choose the Security Key if the sole other option is pairing a new phone. This is an aggressive behavior, so if it skips an option you need, remember that you can hold Shift when the FIDO prompt appears to temporarily disable this program and manually choose a different option.
                
            {processFilename} --priority-file[=$path]
                Reads a priority list from $path (or priority.txt next to this executable, if $path is omitted) that ranks which authenticator option to prefer, such as a third-party passkey provider like 1Password, Bitwarden, or KeePass (see issue #63). Each line is 'Display name as shown in the dialog = priority number', with a higher number meaning more preferred. Special keys are 'USB', 'Pair new phone', and 'Use existing phone'. For example, '1Password = 200' and 'USB = 100' would prefer 1Password when available, and fall back to the USB security key otherwise. Without this file, the default behavior (prefer USB) is used.
                
            {processFilename} --autosubmit-pin-length=$num
                When Windows prompts you for the FIDO PIN for your USB security key, automatically submit the dialog once you have typed a PIN that is $num characters long (minimum 4), instead of you manually pressing Enter. Remember that enough consecutive incorrect submissions (8 on YubiKeys) will permanently block the security key until you reset it and lose all its FIDO credentials, so type with care. This will neither autosubmit PINs when registering a new FIDO credential, changing your PIN, or entering a Windows Hello PIN (which Windows autosubmits without this program's help).
                
            {processFilename} --set-pin
                Prompts you once (in a dialog box, never on a command line) for the PIN of your USB security key, then caches it in memory only (never written to disk) so the program can auto-fill the Windows Security PIN prompt instead of you typing it every time, for --pin-cache-ttl seconds. Because the PIN is only held in memory, you must run --set-pin again after every restart. The cache only works with a single attached security key; if more than one key is present, --set-pin refuses to store the PIN to avoid locking out the wrong key. The same dialog also lets you clear the cached PIN. Without a cached PIN, the program never touches the PIN field.
                
            {processFilename} --pin-cache-ttl=$seconds
                How long (in seconds) a PIN cached with --set-pin stays valid before it expires and you have to cache it again, mirroring gpg-agent's default-cache-ttl. Defaults to 600 (10 minutes). Use 0 to keep the PIN until the program restarts, i.e. it never expires while the program is running.
                
            {processFilename} --pin-clear-on-lock
                Forgets the cached security key PIN whenever Windows is locked, so it has to be re-entered after you unlock.
                
            {processFilename} --pin-clear-on-sleep
            {processFilename} --pin-clear-on-hibernate
                Forgets the cached security key PIN when Windows suspends. Sleep and hibernation both report the same system suspend event, so these two options behave identically.
                
            {processFilename} --log[=$filename]
                Runs this program in the background like the first example, and logs debug messages to a text file. If you don't specify $filename, it goes to {Path.Combine(Environment.GetEnvironmentVariable("TEMP") ?? "%TEMP%", PROGRAM_NAME + ".log")}.
              
            {processFilename} --help
                Shows this usage.
                
            For more information, see https://github.com/f1owkang/{PROGRAM_NAME}.
            Press Ctrl+C to copy this message.
            """, $"{PROGRAM_NAME} {PROGRAM_VERSION} usage", Win32MessageBox.Kind.Information);
    }

    private static void onWindowsLogoff(object sender, SessionEndingEventArgs args) {
        logger?.Info("Exiting due to Windows session ending for {0}", args.Reason);
        SystemEvents.SessionEnding -= onWindowsLogoff;
        requestExit();
    }

    private static void onSessionSwitch(object sender, SessionSwitchEventArgs args) {
        if (args.Reason == SessionSwitchReason.SessionLock && PinCache.clearOnLockEnabled) {
            logger?.Info("Forgetting cached security key PIN because Windows was locked");
            PinCache.clear();
        }
    }

    private static void onPowerModeChanged(object sender, PowerModeChangedEventArgs args) {
        // Sleep and hibernation both surface as the same Suspend event, so either option clears the PIN.
        if (args.Mode == PowerModes.Suspend && (PinCache.clearOnSleepEnabled || PinCache.clearOnHibernateEnabled)) {
            logger?.Info("Forgetting cached security key PIN because Windows is suspending (sleep or hibernation)");
            PinCache.clear();
        }
    }

}