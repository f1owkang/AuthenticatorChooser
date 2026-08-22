namespace AuthenticatorChooser;

internal static class Logging {

    private static string? logFile;

    /// <summary>Starts logging to the console and, when <paramref name="enableFileAppender"/> is set, to a file.</summary>
    public static void initialize(bool enableFileAppender, string? logFilename) {
        logFile = enableFileAppender
            ? logFilename is not null ? Environment.ExpandEnvironmentVariables(logFilename)
                                      : Path.Combine(Path.GetTempPath(), Path.ChangeExtension(nameof(AuthenticatorChooser), ".log"))
            : null;
    }

    internal static void write(string line) {
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