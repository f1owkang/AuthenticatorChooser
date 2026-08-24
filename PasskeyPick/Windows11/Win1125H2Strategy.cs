using System.Windows.Automation;

namespace PasskeyPick.Windows11;

public class Win1125H2Strategy(ChooserOptions options): Win11Strategy(options) {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(Win1125H2Strategy).FullName!);

    private static readonly Condition LINK_CONDITION = new AndCondition(
        new PropertyCondition(AutomationElement.ClassNameProperty, "Hyperlink"),
        AutomationElement.NameProperty.singletonSafeCondition(false, I18N.getStrings(I18N.Key.CHOOSE_A_DIFFERENT_PASSKEY)));

    private static readonly Condition TEXT_BLOCK_CONDITION = new PropertyCondition(AutomationElement.ClassNameProperty, "TextBlock");

    public override bool canHandleTitle(string? actualTitle) => I18N.getStrings(I18N.Key.CHOOSE_A_PASSKEY)
        .Concat(options.skipAllNonSecurityKeyOptions || options.autoSubmitPinLength >= MIN_PIN_LENGTH || PinCache.hasCached() ? I18N.getStrings(I18N.Key.SIGN_IN_WITH_A_PASSKEY) : [])
        .Any(expected => expected.Equals(actualTitle, StringComparison.CurrentCulture));

    public override async Task handleWindow(string actualTitle, AutomationElement fidoEl, AutomationElement outerScrollViewer, bool isShiftDown) {
        if (I18N.getStrings(I18N.Key.CHOOSE_A_PASSKEY).Contains(actualTitle, StringComparer.CurrentCulture)) {
            if (await findAuthenticatorChoices(outerScrollViewer) is not { } authenticatorChoices) return;

            if (getSecurityKeyChoice(authenticatorChoices) is not { } desiredChoice) {
                LOGGER.Debug("Desired choice not found, skipping");
                return;
            }

            if (!shouldSkipSubmission(desiredChoice, authenticatorChoices, isShiftDown)) {
                ((SelectionItemPattern) desiredChoice.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                LOGGER.Info("Choice selected {0:N3} sec after dialog appeared", options.overallStopwatch.Elapsed.TotalSeconds);
            }
        } else {
            /*
             * In 25H2 the security-key challenge (PIN entry) appears inside the "Sign in with a passkey" dialog
             * rather than as a separate prompt. Decide whether this dialog is asking for a security key — its name
             * or the "Security key PIN" label — so a cached PIN can be auto-filled; otherwise it's a TPM prompt,
             * which only --skip-all-non-security-key-options users want to bypass.
             */
            bool isSecurityKeyPrompt = outerScrollViewer.FindAll(TreeScope.Descendants, TEXT_BLOCK_CONDITION)
                .Cast<AutomationElement>()
                .Any(el => I18N.getStrings(I18N.Key.SECURITY_KEY).Any(key => el.Current.Name.Contains(key, StringComparison.CurrentCultureIgnoreCase)));

            if (isSecurityKeyPrompt) {
                if (await tryAutofillPin(fidoEl, null, outerScrollViewer)) {
                    return;
                }
                if (options.autoSubmitPinLength >= MIN_PIN_LENGTH) {
                    autosubmitPin(fidoEl, outerScrollViewer);
                } else {
                    LOGGER.Debug("The current authenticator is already a security key, so there is nothing to do on this dialog");
                }
                return;
            }

            // Only users who asked to skip every non-security-key option get the "choose a different passkey" skip;
            // a cached PIN or autosubmit alone must not silently bypass a TPM prompt.
            if (!options.skipAllNonSecurityKeyOptions) {
                LOGGER.Debug("The current authenticator is not a security key, leaving the dialog untouched");
                return;
            }

            if (outerScrollViewer.FindFirst(TreeScope.Children, LINK_CONDITION) is not { } chooseADifferentPasskeyLink) {
                LOGGER.Warn("Could not find 'Choose a different passkey' link in dialog");
                return;
            }

            ((InvokePattern) chooseADifferentPasskeyLink.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            LOGGER.Info("Requested list of all authenticators {0:N3} sec after dialog appeared", options.overallStopwatch.Elapsed.TotalSeconds);
        }
    }

}