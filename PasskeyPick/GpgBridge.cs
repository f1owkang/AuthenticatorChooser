using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace PasskeyPick;

/// <summary>In-process TCP bridge (issue #7): listens on 127.0.0.1 and relays byte streams to the local gpg-agent's
/// simulated-UDS extra socket, performing the 16-byte nonce handshake per connection. Enables remote Git signing over
/// an SSH RemoteForward tunnel. Loopback only; off by default (opt-in). Enabling it is equivalent to ssh -A: any
/// process on the machine (including other accounts) can then sign through the user's gpg-agent, so enabling warns
/// (tray balloon) and every accepted connection is audited (source, first Assuan instruction, byte counts).</summary>
internal static class GpgBridge {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(GpgBridge).FullName!);

    private static readonly object sync = new();
    private static TcpListener? listener;
    private static CancellationTokenSource? stopAccepting;

    public static bool isRunning => listener is not null;

    /// <summary>Last bind/start error (e.g. port already in use), for the settings-dialog status section.</summary>
    public static string? lastError { get; private set; }

    /// <summary>Connections that send nothing within this window are closed (idle probe / dead tunnel guard).</summary>
    private static readonly TimeSpan IDLE_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>Starts listening on 127.0.0.1:<paramref name="port"/>. Idempotent: any existing bridge is stopped first.
    /// A tray-balloon warning is shown when the bridge transitions from stopped to running (enable at runtime, or the
    /// app started with the bridge already enabled), so the user is never silently exposed.</summary>
    public static void start(int port) {
        lock (sync) {
            bool wasRunning = listener is not null;
            if (listener is not null) {
                stop();
            }
            lastError = null;
            try {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                stopAccepting = CancellationTokenSource.CreateLinkedTokenSource(Startup.EXITING);
                _ = Task.Run(() => acceptLoop(listener, stopAccepting.Token));
                LOGGER.Info("gpg-agent TCP bridge listening on 127.0.0.1:{port}", port);
                if (!wasRunning) {
                    TrayNotifications.show("gpgBridgeEnabledTitle", "gpgBridgeEnabledBody", ToolTipIcon.Warning, port);
                }
            } catch (Exception e) when (e is not OutOfMemoryException) {
                lastError = e.Message;
                LOGGER.Error(e, "gpg-agent TCP bridge could not listen on 127.0.0.1:{port}", port);
                listener = null;
            }
        }
    }

    public static void startIfEnabled() {
        if (Settings.gpgBridgeEnabled) {
            start(Settings.gpgBridgePort);
        }
    }

    public static void stop() {
        lock (sync) {
            stopAccepting?.Cancel();
            stopAccepting?.Dispose();
            stopAccepting = null;
            try { listener?.Stop(); } catch { }
            listener = null;
        }
    }

    private static async Task acceptLoop(TcpListener l, CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            TcpClient client;
            try {
                client = await l.AcceptTcpClientAsync(ct);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception e) when (e is not OutOfMemoryException) {
                LOGGER.Warn("gpg-agent TCP bridge accept failed: {message}", e.Message);
                await Task.Delay(200, CancellationToken.None);
                continue;
            }
            _ = Task.Run(() => relay(client), CancellationToken.None);
        }
    }

    private static async Task relay(TcpClient client) {
        using (client) {
            try {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(Startup.EXITING);
                // Fresh (port, nonce) per connection — the agent restarts change both, so nothing is cached.
                if (getSocketEndpoint() is not { } endpoint) {
                    return;
                }
                using var agent = new TcpClient();
                await agent.ConnectAsync(IPAddress.Loopback, endpoint.port, cts.Token);
                await agent.GetStream().WriteAsync(endpoint.nonce, cts.Token);
                // Connection audit: read the client's first chunk, log the source and the first Assuan instruction
                // (SIGN, HAVEKEY, DECRYPT, ...), then forward it — lets the user spot unexpected sign/decrypt requests
                // after the fact instead of only trusting that nothing hostile reached the agent.
                string? remote = client.Client.RemoteEndPoint?.ToString();
                int    firstBytes = await auditFirstChunk(client, agent.GetStream(), cts.Token);
                if (firstBytes < 0) {
                    return; // idle connection closed without relaying anything
                }
                Task<long> a = copy(client, agent, cts.Token);
                Task<long> b = copy(agent, client, cts.Token);
                Task completed = await Task.WhenAny(a, b);
                if (completed.IsFaulted) {
                    // One direction faulted (e.g. connection reset on tunnel teardown): cancel the sibling so the two
                    // open sockets close promptly instead of hanging until the peer closes or the app exits.
                    cts.Cancel();
                }
                await Task.WhenAll(a, b); // both directions must EOF/fault for the connection to end
                LOGGER.Info("gpg-agent bridge connection from {remote} closed ({sent} bytes to agent, {received} bytes to client)",
                    remote, firstBytes + await a, await b);
            } catch (Exception e) when (e is not OutOfMemoryException) {
                LOGGER.Warn("gpg-agent bridge relay failed: {message}", e.Message);
            }
        }
    }

    /// <summary>Reads the client's first chunk, logs an audit line (source, first Assuan instruction, chunk size), and
    /// forwards that chunk to the agent so the relay loses nothing. A connection that accepts but sends nothing within
    /// <see cref="IDLE_TIMEOUT"/> is a probe or a dead tunnel: it is closed (returns -1) instead of pinning a relay
    /// task forever, so the accept loop cannot be exhausted by idle connections.</summary>
    private static async Task<int> auditFirstChunk(TcpClient client, NetworkStream agentStream, CancellationToken ct) {
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idleCts.CancelAfter(IDLE_TIMEOUT);
        var buffer = new byte[512];
        int n;
        try {
            n = await client.GetStream().ReadAsync(buffer.AsMemory(0, buffer.Length), idleCts.Token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            LOGGER.Warn("gpg-agent bridge closed connection from {remote} after {seconds} s idle with no data",
                client.Client.RemoteEndPoint, (int) IDLE_TIMEOUT.TotalSeconds);
            return -1; // abort the relay
        }
        if (n == 0) {
            return 0;
        }
        // The agent protocol (Assuan) is line-based; the first whitespace-delimited token is the instruction.
        int tokenLength = 0;
        while (tokenLength < n && tokenLength < 32) {
            byte b = buffer[tokenLength];
            if (b is (byte) ' ' or (byte) '\t' or (byte) '\r' or (byte) '\n') {
                break;
            }
            tokenLength++;
        }
        string instruction = tokenLength > 0 ? Encoding.UTF8.GetString(buffer, 0, tokenLength) : "?";
        LOGGER.Info("gpg-agent bridge accepted connection from {remote}: first instruction {instruction}, first chunk {bytes} bytes",
            client.Client.RemoteEndPoint, instruction, n);
        await agentStream.WriteAsync(buffer.AsMemory(0, n), ct);
        return n;
    }

    private static async Task<long> copy(TcpClient from, TcpClient to, CancellationToken ct) {
        var buffer = new byte[4096];
        long total = 0;
        try {
            while (true) {
                int read = await from.GetStream().ReadAsync(buffer, ct);
                if (read == 0) {
                    // #7: half-close the send side so the peer sees EOF while the reverse relay stays open.
                    try { to.Client.Shutdown(SocketShutdown.Send); } catch { }
                    return total;
                }
                await to.GetStream().WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
            }
        } catch (OperationCanceledException) {
            return total;
        }
    }

    private static (int port, byte[] nonce)? getSocketEndpoint() {
        string? socketPath = GpgTools.getExtraSocketPath();
        if (socketPath is null || !File.Exists(socketPath)) {
            // #7: start the agent on demand, then re-read.
            if (!GpgTools.startAgent()) {
                return null;
            }
            socketPath = GpgTools.getExtraSocketPath();
        }
        return socketPath is null ? null : GpgTools.parseSocketFile(socketPath);
    }
}
