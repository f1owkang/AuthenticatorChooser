using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace AuthenticatorChooser;

/// <summary>
/// <para>Parses and applies a configurable priority list for choosing among authenticator options, including
/// third-party passkey providers like 1Password, Bitwarden, KeePass, etc. (issue #63).</para>
/// <para>
/// Config file format (<c>priority.txt</c> by default, next to the executable, or overridden with
/// <c>--priority-file</c>), one rule per line:
/// <code>
///   Display name as it appears in the dialog = unsigned integer priority
/// </code>
/// The higher the number, the more preferred the option. Three special keys have fixed meanings:
/// <list type="bullet">
///   <item><c>USB</c> — the USB security key option</item>
///   <item><c>Pair new phone</c> — pairing a new Bluetooth phone</item>
///   <item><c>Use existing phone</c> — an already-paired Bluetooth phone</item>
/// </list>
/// Every other key is matched case-insensitively against the option text as it appears in the dialog.
/// If no rules are configured, the default behavior is to prefer the USB security key, as before.
/// </para>
/// </summary>
public static class PriorityChooser {

    /// <summary>Special key representing the USB security key option.</summary>
    public const string USB_KEY = "USB";

    /// <summary>Special key representing the option to pair a new Bluetooth phone.</summary>
    public const string PAIR_NEW_PHONE_KEY = "Pair new phone";

    /// <summary>Special key representing the option of an already-paired Bluetooth phone.</summary>
    public const string USE_EXISTING_PHONE_KEY = "Use existing phone";

    /// <summary>Default filename (in the same directory as the executable) when no <c>--priority-file</c> is given.</summary>
    public const string DEFAULT_FILENAME = "priority.txt";

    private static readonly Regex LINE_PATTERN = new(@"^\s*(?<name>.+?)\s*=\s*(?<priority>\d+)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Loads priority rules from <paramref name="path"/>. Returns an empty dictionary if the file is missing,
    /// unreadable, or contains no valid rules — never throws, because the program must keep running.
    /// </summary>
    public static IReadOnlyDictionary<string, int> load(string path) {
        try {
            if (!File.Exists(path)) {
                return new Dictionary<string, int>();
            }

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in File.ReadLines(path)) {
                // Strip inline comments (a '#' outside of quotes) and whitespace
                string line = stripComment(rawLine).Trim();
                if (line.Length == 0) {
                    continue; // skip blank and comment-only lines
                }

                Match match = LINE_PATTERN.Match(line);
                if (!match.Success) {
                    continue; // skip malformed lines
                }

                string name     = match.Groups["name"].Value.Trim();
                int    priority = int.Parse(match.Groups["priority"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                if (name.Length > 0) {
                    result[name] = priority;
                }
            }
            return result;
        } catch (Exception) {
            // A malformed or unreadable config file must not crash the program
            return new Dictionary<string, int>();
        }
    }

    private static string stripComment(string line) {
        int hashIndex = line.IndexOf('#');
        return hashIndex >= 0 ? line[..hashIndex] : line;
    }

    /// <summary>
    /// Given the actual authenticator choices in the dialog and the configured priority rules, decides which
    /// choice to select. Returns <c>null</c> when no configured option matches and there is no USB fallback.
    /// </summary>
    /// <param name="authenticatorChoices">All authenticator option elements currently shown in the dialog.</param>
    /// <param name="rules">The loaded priority rules (may be empty).</param>
    /// <param name="securityKeyStrings">Localized substrings that identify the USB security key option.</param>
    /// <param name="smartphoneStrings">Localized substrings that identify the "pair a new phone" option.</param>
    public static AutomationElement? chooseBest(IReadOnlyCollection<AutomationElement> authenticatorChoices, IReadOnlyDictionary<string, int> rules,
        IEnumerable<string> securityKeyStrings, IEnumerable<string> smartphoneStrings) {

        if (rules.Count == 0) {
            // Default behavior (unchanged from before issue #63): prefer USB security key
            return authenticatorChoices.FirstOrDefault(choice => choice.nameContainsAny(securityKeyStrings));
        }

        // Rank every visible choice by the configured priority, falling back to USB / pair-new-phone defaults.
        AutomationElement? best = null;
        int                bestPriority = int.MinValue;

        foreach (AutomationElement choice in authenticatorChoices) {
            string choiceName = choice.Current.Name;
            int    priority   = getPriority(choiceName, rules, securityKeyStrings, smartphoneStrings);
            if (priority > bestPriority) {
                best          = choice;
                bestPriority = priority;
            }
        }

        return best;
    }

    private static int getPriority(string choiceName, IReadOnlyDictionary<string, int> rules, IEnumerable<string> securityKeyStrings, IEnumerable<string> smartphoneStrings) {
        // 1. Exact or substring match against a configured named rule wins first
        foreach ((string ruleName, int rulePriority) in rules) {
            if (ruleName.Equals(choiceName, StringComparison.OrdinalIgnoreCase) || choiceName.Contains(ruleName, StringComparison.OrdinalIgnoreCase)) {
                return rulePriority;
            }
        }

        // 2. Fall back to the built-in defaults for the known special options
        if (securityKeyStrings.Any(s => choiceName.Contains(s, StringComparison.CurrentCulture))) {
            return rules.TryGetValue(USB_KEY, out int usbPriority) ? usbPriority : 100;
        }
        if (smartphoneStrings.Any(s => choiceName.Contains(s, StringComparison.CurrentCulture))) {
            return rules.TryGetValue(PAIR_NEW_PHONE_KEY, out int pairPriority) ? pairPriority : 0;
        }

        // 3. Unknown, unconfigured option: effectively neutral
        return 0;
    }

}
