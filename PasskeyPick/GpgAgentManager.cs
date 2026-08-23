using System.Threading;
using System.Windows.Forms;

namespace PasskeyPick;

/// <summary>Starts the Gpg4win gpg-agent at app startup and keeps it alive (issue #8): probes the extra-socket TCP port
/// every 30 s and restarts the agent via gpg-connect-agent /bye when it is unreachable. Skips silently when Gpg4win is
/// absent or the feature is disabled.</summary>
internal static class GpgAgentManager {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(GpgAgentManager).FullName!);

    private static readonly object sync = new();
    private static CancellationTokenSource? keepAliveCts;

    /// <summary>Whether the keep-alive management loop is running.</summary>
    public static bool isActive => keepAliveCts is not null;

    public static void startIfEnabled() {
        lock (sync) {
            if (!Settings.gpgAgentAutostartEnabled || keepAliveCts is not null) {
                return;
            }
            if (GpgTools.resolveGpg4winConnectAgent() is null) {
                LOGGER.Warn("gpg-agent autostart skipped: no Gpg4win build found");
                return;
            }
            GpgTools.startAgent();
            keepAliveCts = CancellationTokenSource.CreateLinkedTokenSource(Startup.EXITING);
            _ = Task.Run(() => keepAliveLoop(keepAliveCts.Token));
            LOGGER.Info("gpg-agent keep-alive management started");
        }
    }

    public static void stop() {
        lock (sync) {
            keepAliveCts?.Cancel();
            keepAliveCts?.Dispose();
            keepAliveCts = null;
        }
    }

    private static async Task keepAliveLoop(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            try {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            } catch (OperationCanceledException) {
                break;
            }
            try {
                if (!GpgTools.probeAgent()) {
                    LOGGER.Warn("gpg-agent unreachable, restarting via gpg-connect-agent /bye");
                    if (GpgTools.startAgent()) {
                        TrayNotifications.show("gpgAgentRestartedTitle", "gpgAgentRestartedBody", ToolTipIcon.Info);
                    }
                }
            } catch (Exception e) when (e is not OutOfMemoryException) {
                LOGGER.Error(e, "gpg-agent keep-alive loop iteration failed");
            }
        }
    }
}
