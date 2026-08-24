namespace PasskeyPick.Tests;

public class PriorityChooserTests: IDisposable {

    private readonly List<string> tempFiles = [];

    public void Dispose() {
        foreach (string path in tempFiles) {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Load_ParsesRulesAndStripsComments() {
        string path = writeTemp("""
            # a comment-only line
            1Password = 200   # an inline comment
            "KeePass # Pro" = 150
            garbage line without an equals sign
            = 5
            BadPriority = x
            USB = 100
            """);

        IReadOnlyDictionary<string, int> rules = PriorityChooser.load(path);

        Assert.Equal(3, rules.Count);
        Assert.Equal(200, rules["1Password"]);
        Assert.Equal(150, rules["KeePass # Pro"]); // '#' inside quotes survives the comment stripping
        Assert.Equal(100, rules["USB"]);
        Assert.Equal(200, rules["1password"]);     // keys match case-insensitively
    }

    [Fact]
    public void Load_MissingFileYieldsNoRules() =>
        Assert.Empty(PriorityChooser.load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

    private static readonly string[] SECURITY_KEY = ["Security key"];
    private static readonly string[] SMARTPHONE   = ["Pair new phone"];

    [Fact]
    public void GetPriority_ExactRuleMatchWins() {
        var rules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["1Password"] = 200 };
        Assert.Equal(200, PriorityChooser.getPriority("1Password", rules, SECURITY_KEY, SMARTPHONE));
        Assert.Equal(200, PriorityChooser.getPriority("1PASSWORD", rules, SECURITY_KEY, SMARTPHONE));
    }

    [Fact]
    public void GetPriority_SubstringDoesNotMatch() {
        // 'Bit = 300' must not capture 'Bitwarden'; rule names match the full option text only.
        var rules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Bit"] = 300 };
        Assert.Equal(0, PriorityChooser.getPriority("Bitwarden", rules, SECURITY_KEY, SMARTPHONE));
    }

    [Fact]
    public void GetPriority_UsbFallsBackToDefault100() {
        var rules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(100, PriorityChooser.getPriority("Security key (USB)", rules, SECURITY_KEY, SMARTPHONE));
    }

    [Fact]
    public void GetPriority_UsbRuleOverridesDefault() {
        var rules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [PriorityChooser.USB_KEY] = 250 };
        Assert.Equal(250, PriorityChooser.getPriority("Security key (USB)", rules, SECURITY_KEY, SMARTPHONE));
    }

    [Fact]
    public void GetPriority_PairNewPhoneDefaultsToNeutral() {
        var rules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, PriorityChooser.getPriority("Pair new phone", rules, SECURITY_KEY, SMARTPHONE));
    }

    [Fact]
    public void GetPriority_UnknownOptionIsNeutral() {
        var rules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["1Password"] = 200 };
        Assert.Equal(0, PriorityChooser.getPriority("Windows Hello", rules, SECURITY_KEY, SMARTPHONE));
    }

    private string writeTemp(string content) {
        string path = Path.Combine(Path.GetTempPath(), $"priority-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        tempFiles.Add(path);
        return path;
    }

}
