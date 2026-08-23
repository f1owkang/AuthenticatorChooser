using System.Diagnostics;

namespace PasskeyPick;

public sealed record ChooserOptions(bool skipAllNonSecurityKeyOptions, int? autoSubmitPinLength, string? priorityFile) {

    public Stopwatch overallStopwatch { get; } = new();

    /// <summary>
    /// <para>Whether the program should currently interact with FIDO prompt dialogs. Toggled by the system tray
    /// icon (issue #57). When <see langword="false"/>, all FIDO dialogs are left completely untouched.</para>
    /// <para>This is deliberately a mutable, thread-safe flag, because it is read from the UI Automation message-loop
    /// thread and written from the tray icon's context menu.</para>
    /// </summary>
    private volatile bool enabled = true;

    public bool isEnabled {
        get => enabled;
        set => enabled = value;
    }

    /// <summary>
    /// <para>The display name of the authenticator method chosen by the user from the system tray icon's
    /// "Preferred authenticator" submenu (issue #5), or <see langword="null"/> for the default (automatic)
    /// behavior. This is deliberately a mutable, thread-safe flag, because it is read from the UI Automation
    /// message-loop thread and written from the tray icon's context menu.</para>
    /// </summary>
    private volatile string? preferred;

    public string? preferredAuthenticator {
        get => preferred;
        set => preferred = value;
    }

}