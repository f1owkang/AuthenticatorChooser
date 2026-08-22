using System.Text.RegularExpressions;

namespace AuthenticatorChooser;

/// <summary>A tiny logger that replaces NLog, keeping the same <c>Logger.Info/Debug/Warn/Error(...)</c> call shape and
/// NLog-style <c>{name}</c> / <c>{0:format}</c> message templates, so the rest of the app is untouched.</summary>
internal sealed class Logger {

    private readonly string name;

    internal Logger(string name) => this.name = name;

    public void Trace(string message, params object?[] args) => log("TRC", message, args);
    public void Debug(string message, params object?[] args) => log("DBG", message, args);
    public void Info(string message, params object?[] args) => log("INF", message, args);
    public void Warn(string message, params object?[] args) => log("WRN", message, args);
    public void Error(string message, params object?[] args) => log("ERR", message, args);

    public void Error(object message, params object?[] args) => log("ERR", message.ToString() ?? "", args);

    public void Error(Exception exception, string? message = null, params object?[] args) =>
        log("ERR", (string.IsNullOrEmpty(message) ? exception.ToString() : message + Environment.NewLine + exception), args);

    private void log(string level, string message, object?[] args) =>
        Logging.write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{name}] {format(message, args)}");

    private static readonly Regex TEMPLATE = new(@"\{([^{}:]+?)(?::([^}]+))?\}");

    private static string format(string message, object?[] args) {
        if (args.Length == 0) {
            return message;
        }
        int nextNamed = 0;
        return TEMPLATE.Replace(message, match => {
            string key  = match.Groups[1].Value;
            string fmt  = match.Groups[2].Success ? ":" + match.Groups[2].Value : "";
            int    index = int.TryParse(key, out int parsed) ? parsed : nextNamed++;
            return index < args.Length ? string.Format($"{{{index}{fmt}}}", args) : match.Value;
        });
    }

}

/// <summary>Drop-in replacement for NLog's <see cref="LogManager"/> factory.</summary>
internal static class LogManager {

    internal static Logger GetLogger(string name) => new(name);

    internal static void Shutdown() { }

}
