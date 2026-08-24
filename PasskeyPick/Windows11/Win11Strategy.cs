using System.Windows.Automation;

namespace PasskeyPick.Windows11;

public abstract class Win11Strategy(ChooserOptions options): PromptStrategy {

    protected const int MIN_PIN_LENGTH = 4; // https://support.yubico.com/hc/en-us/articles/4402836718866-Understanding-YubiKey-PINs#h_01HPHYDEAT97H0AJ4SZ48MWHW4

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(Win11Strategy).FullName!);

    private static readonly   Condition CHOICES_LIST_CONDITION = new PropertyCondition(AutomationElement.ClassNameProperty, "ListView");
    protected static readonly Condition NEXT_BUTTON_CONDITION  = new PropertyCondition(AutomationElement.AutomationIdProperty, "OkButton");

    protected ChooserOptions options { get; } = options;

    public abstract bool canHandleTitle(string? actualTitle);
    public abstract Task handleWindow(string actualTitle, AutomationElement fidoEl, AutomationElement outerScrollViewer, bool isShiftDown);

    protected bool shouldSkipSubmission(AutomationElement desiredChoice, IEnumerable<AutomationElement> authenticatorChoices, bool isShiftDown) {
        if (isShiftDown) {
            LOGGER.Info("Shift is pressed, not submitting dialog box");
            return true;
        } else if (options.preferredAuthenticator != null) {
            // #5: the user explicitly chose a preferred authenticator from the tray menu, so submit it
            return false;
        } else if (options.priorityFile != null) {
            // #63: a user-configured priority list is authoritative, so never skip a matched choice
            return false;
        } else if (!options.skipAllNonSecurityKeyOptions && !authenticatorChoices.All(choice => choice == desiredChoice || choice.nameContainsAny(I18N.getStrings(I18N.Key.SMARTPHONE)))) {
            LOGGER.Info(
                "Dialog box has a choice that is neither pairing a new phone nor USB security key (such as an existing phone, PIN, biometrics, or a third-party passkey provider), skipping because you might want to choose it. You may override this behavior with --skip-all-non-security-key-options, or define a --priority-file to choose which option to prefer.");
            return true;
        } else {
            return false;
        }
    }

    protected static async Task<IReadOnlyCollection<AutomationElement>?> findAuthenticatorChoices(AutomationElement outerScrollViewer, CancellationToken ct = default) {
        using CancellationTokenSource stopFinding = CancellationTokenSource.CreateLinkedTokenSource(Startup.EXITING, ct);
        IReadOnlyList<AutomationElement>? authenticatorChoices =
            await outerScrollViewer.WaitForFirstAsync(TreeScope.Children, CHOICES_LIST_CONDITION, el => Task.FromResult(el.Children().ToList()), TimeSpan.FromSeconds(30), stopFinding.Token);
        if (authenticatorChoices == null) {
            LOGGER.Warn("Could not find authenticator choices after retrying for 1 minute. Giving up and not automatically selecting Security Key.");
        }
        return authenticatorChoices;
    }

    protected AutomationElement? getSecurityKeyChoice(IEnumerable<AutomationElement> authenticatorChoices) {
        IReadOnlyCollection<AutomationElement> choices = authenticatorChoices as IReadOnlyCollection<AutomationElement> ?? authenticatorChoices.ToList();

        // #5: an authenticator chosen from the system tray menu takes priority over everything else
        if (options.preferredAuthenticator is { } preferredName) {
            AutomationElement? preferredChoice = PriorityChooser.chooseBest(choices,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [preferredName] = 1000 },
                I18N.getStrings(I18N.Key.SECURITY_KEY), I18N.getStrings(I18N.Key.SMARTPHONE));
            if (preferredChoice != null) {
                LOGGER.Info("Selected authenticator option \"{name}\" chosen from the system tray menu", preferredChoice.Current.Name);
                return preferredChoice;
            }
            LOGGER.Debug("Preferred authenticator \"{name}\" from the tray menu is not present in this dialog, falling back", preferredName);
        }

        if (options.priorityFile != null) {
            // #63: consult the user-configured priority list, falling back to USB if nothing else is preferred
            AutomationElement? preferred = PriorityChooser.chooseBest(choices,
                PriorityChooser.load(options.priorityFile), I18N.getStrings(I18N.Key.SECURITY_KEY), I18N.getStrings(I18N.Key.SMARTPHONE));
            if (preferred != null) {
                LOGGER.Info("Selected authenticator option \"{name}\" according to priority list", preferred.Current.Name);
            }
            return preferred;
        }

        return authenticatorChoices.FirstOrDefault(choice => choice.nameContainsAny(I18N.getStrings(I18N.Key.SECURITY_KEY)));
    }

    protected static async Task<AutomationElement?> findPinField(AutomationElement outerScrollViewer, CancellationToken ct) =>
        await outerScrollViewer.WaitForFirstAsync(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsPasswordProperty, true),
            TimeSpan.FromMinutes(3), ct);

    /// <summary>
    /// If a PIN was cached with <c>--set-pin</c> and is still within its cache TTL, fills it into the Windows Security
    /// PIN prompt and submits the dialog, so the user doesn't have to type the PIN on every assertion (modeled on
    /// gpg-agent's passphrase cache). The PIN travels as an unmanaged <c>BSTR</c> into the native
    /// <c>IUIAutomationValuePattern::SetValue</c> (see <see cref="NativeUia"/>), never as a managed string. Returns
    /// <see langword="true"/> when the dialog was auto-filled and submitted.
    /// <paramref name="pinField"/> may already be known (e.g. from <see cref="findPinField"/>); otherwise it is looked
    /// up under <paramref name="outerScrollViewer"/>.
    /// </summary>
    protected async Task<bool> tryAutofillPin(AutomationElement fidoEl, AutomationElement? pinField, AutomationElement? outerScrollViewer = null) {
        pinField ??= outerScrollViewer is null ? null : await findPinField(outerScrollViewer, Startup.EXITING);
        if (pinField is null || !PinCache.hasCached()) {
            return false;
        }

        bool filled;
        try {
            pinField.SetFocus();
            IntPtr windowHandle = new(fidoEl.Current.NativeWindowHandle);
            if (windowHandle == IntPtr.Zero) {
                LOGGER.Warn("The FIDO dialog has no native window handle, cannot auto-fill the PIN; please type the PIN manually");
                return false;
            }
            filled = PinCache.tryUseCachedPin(bstr => {
                // Re-verify trust at fill time, not just at detection time: findPinField may have waited up to 3
                // minutes, during which the real dialog could have closed and its HWND been reused by an untrusted
                // process faking the "Credential Dialog Xaml Host" class to steal the PIN (TOCTOU).
                if (!WindowTrust.isTrustedSystemProcess(windowHandle)) {
                    LOGGER.Warn("The FIDO dialog window is no longer owned by a trusted system process; refusing to fill the cached PIN");
                    return false;
                }
                return NativeUia.setPasswordValue(windowHandle, bstr);
            });
        } catch (Exception e) when (e is not OutOfMemoryException) {
            // SendKeys is a global keyboard-injection primitive that does not verify which window has focus, so it is
            // never a safe fallback: the PIN could be typed into whatever window happens to be focused (e.g. a browser
            // or chat box). Ask the user to type the PIN manually instead.
            LOGGER.Warn("UIA SetValue for the PIN field failed ({message}), skipping auto-fill; please type the PIN manually", e.Message);
            return false;
        }
        if (!filled) {
            return false;
        }
        LOGGER.Info("Auto-filled cached security key PIN {0:N3} sec after dialog appeared", options.overallStopwatch.Elapsed.TotalSeconds);

        if (fidoEl.FindFirst(TreeScope.Children, NEXT_BUTTON_CONDITION) is { } okButton) {
            ((InvokePattern) okButton.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            LOGGER.Info("Submitted security key PIN {0:N3} sec after dialog appeared", options.overallStopwatch.Elapsed.TotalSeconds);
            return true;
        }
        return false;
    }

    protected void autosubmitPin(AutomationElement fidoEl, AutomationElement outerScrollViewer, AutomationElement? pinField = null) {
        CancellationTokenSource windowClosed = new();
        Automation.AddAutomationEventHandler(WindowPattern.WindowClosedEvent, fidoEl, TreeScope.Element, cleanUp);

        Task.Run(async () => {
            LOGGER.Debug("Waiting for security key PIN prompt to appear");
            pinField ??= await findPinField(outerScrollViewer, windowClosed.Token);

            if (pinField != null) {
                Automation.AddAutomationPropertyChangedEventHandler(pinField, TreeScope.Descendants, onPinTyped, ValuePattern.ValueProperty);

                // skipping this current value read seems to also prevent any events from being fired for some reason
                onPinTyped(this, new AutomationPropertyChangedEventArgs(ValuePattern.ValueProperty, null, ((ValuePattern) pinField.GetCurrentPattern(ValuePattern.Pattern)).Current.Value));
                LOGGER.Debug("Found security key PIN prompt, waiting for the user to type {0:N0} characters before submitting it", options.autoSubmitPinLength);
            } else {
                LOGGER.Debug("No security key PIN prompt found");
            }
        }, windowClosed.Token);

        void onPinTyped(object sender, AutomationPropertyChangedEventArgs e) {
            try {
                int typedPinLength = ((string) e.NewValue).Length;
                if (typedPinLength == options.autoSubmitPinLength) {
                    LOGGER.Info("Submitting security key PIN prompt because the user typed {0:N0} characters", typedPinLength);
                    cleanUp();
                    AutomationElement okButton = fidoEl.FindFirst(TreeScope.Children, NEXT_BUTTON_CONDITION);
                    ((InvokePattern) okButton.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                }
            } catch (Exception exception) when (exception is not OutOfMemoryException) {
                LOGGER.Error(e);
            }
        }

        void cleanUp(object? sender = null, AutomationEventArgs? e = null) {
            Automation.RemoveAutomationPropertyChangedEventHandler(fidoEl, onPinTyped);
            Automation.RemoveAutomationEventHandler(WindowPattern.WindowClosedEvent, fidoEl, cleanUp);
            windowClosed.Cancel();
            windowClosed.Dispose();
            if (sender != null) {
                LOGGER.Debug("Security key PIN window closed");
            }
        }
    }

}