namespace PasskeyPick;

internal static class Logging {

    private static string? logFile;

    /// <summary>Starts logging to the console and, when <paramref name="enableFileAppender"/> is set, to a file.</summary>
    public static void initialize(bool enableFileAppender, string? logFilename) {
        logFile = enableFileAppender
            ? logFilename is not null ? Environment.ExpandEnvironmentVariables(logFilename)
                                      : Path.Combine(Path.GetTempPath(), Path.ChangeExtension(nameof(PasskeyPick), ".log"))
            : null;
    }

    internal static void write(string line) {
        // Neutralize CR/LF, ANSI escapes, and Unicode line separators so attacker-controlled text (window titles,
        // provider names) cannot forge log lines or spoof terminal output (CWE-117).
        line = line.Replace("\r", "\\r").Replace("\n", "\\n")
                   .Replace("\u001b", "\\e")
                   .Replace("\u0085", "\\u0085")
                   .Replace("\u2028", "\\u2028")
                   .Replace("\u2029", "\\u2029");
        Console.WriteLine(line);
        if (logFile != null) {
            try {
                File.AppendAllText(logFile, line + Environment.NewLine);
            } catch (Exception) {
                // logging must never crash the program
            }
        }
    }

}