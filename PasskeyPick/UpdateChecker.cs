using System.Reflection;
using System.Text.Json;

namespace PasskeyPick;

/// <summary>Checks GitHub releases for a newer version and reports it, without ever downloading or installing.</summary>
internal static class UpdateChecker {

    private const string REPO       = "f1owkang/PasskeyPick";
    private const string USER_AGENT = "PasskeyPick (https://github.com/f1owkang/PasskeyPick)";

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(UpdateChecker).FullName!);

    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };

    internal static readonly string LATEST_RELEASE_URL = $"https://github.com/{REPO}/releases/latest";

    /// <summary>True when the last check was more than 24 hours ago (or never ran), so the API is not spammed.</summary>
    internal static bool isCheckStale() =>
        Settings.lastUpdateCheckUtc is not { } last || DateTime.UtcNow - last > TimeSpan.FromHours(24);

    internal static void markChecked() {
        Settings.lastUpdateCheckUtc = DateTime.UtcNow;
        Settings.save();
    }

    /// <summary>Returns the latest release tag if it is newer than the running version, else <see langword="null"/>.
    /// Any network or parsing failure also returns <see langword="null"/> so the tray app is never disturbed.</summary>
    internal static async Task<string?> getNewerReleaseTagAsync() {
        try {
            using HttpRequestMessage request = new(HttpMethod.Get, $"https://api.github.com/repos/{REPO}/releases/latest");
            request.Headers.UserAgent.ParseAdd(USER_AGENT);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await http.SendAsync(request, Startup.EXITING);
            if (!response.IsSuccessStatusCode) {
                LOGGER.Warn("Update check failed: GitHub API returned {status}", response.StatusCode);
                return null;
            }

            string? tag = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Startup.EXITING)).RootElement.GetProperty("tag_name").GetString();
            if (tag is null || Assembly.GetEntryAssembly()!.GetName().Version is not { } current) {
                return null;
            }

            return Version.TryParse(tag.TrimStart('v'), out Version? latest) && latest > current ? tag : null;
        } catch (Exception e) when (e is not OutOfMemoryException) {
            LOGGER.Warn("Update check failed: {message}", e.Message);
            return null; // never let a network problem interrupt the tray app
        }
    }

}
