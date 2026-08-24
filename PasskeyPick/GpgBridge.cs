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

    /// <summary>Cap on simultaneous relay connections, so a local process cannot exhaust handles/threads by opening
    /// the loopback port in a loop (local DoS only; a rejected connector just retries or fails).</summary>
    private const int MAX_CONCURRENT_CONNECTIONS = 16;
    private static readonly SemaphoreSlim connectionSlots = new(MAX_CONCURRENT_CONNECTIONS, MAX_CONCURRENT_CONNECTIONS);

    public static bool isRunning => listener is not null;

    /// <summary>Last bind/start error (e.g. port already in use), for the settings-dialog status section.</summary>
    public static string? lastError { get; private set; }

    /// <summary>Connections that accept but whose client never speaks within this window are closed (idle probe / dead
    /// tunnel guard). It only applies until the client's first data is relayed, so a legitimate handshake or a slow
    /// card touch / PIN prompt during signing is never killed. Internal and mutable only so tests can shorten it.</summary>
    internal static TimeSpan IDLE_TIMEOUT = TimeSpan.FromSeconds(30);
    private static long idleTimeoutMillis => (long) IDLE_TIMEOUT.TotalMilliseconds;

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
            if (!connectionSlots.Wait(0)) {
                LOGGER.Warn("gpg-agent TCP bridge rejecting connection: {max} connections already active", MAX_CONCURRENT_CONNECTIONS);
                client.Dispose();
                continue;
            }
            _ = Task.Run(async () => {
                try {
                    await relay(client);
                } finally {
                    connectionSlots.Release();
                }
            }, CancellationToken.None);
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

                // Both relay directions start immediately after the nonce, so the agent's assuan greeting
                // ("OK Pleased to meet you …") flows to the client without waiting for client input. Issue #9: the
                // audit used to pre-read the client's first chunk first, but libassuan does not send anything until it
                // has seen the greeting, so the two sides deadlocked and the 30 s idle timer killed the connection.
                // The audit now runs inside the client→agent copy as data passes, so it never blocks the handshake.
                var counters = new RelayCounters();
                string? remote = client.Client.RemoteEndPoint?.ToString();
                Task watchdog = idleWatchdog(counters, cts, remote);
                Task a = copy(client, agent, cts.Token, counters, auditFirstChunk: true, remote);
                Task b = copy(agent, client, cts.Token, counters, auditFirstChunk: false, remote);
                try {
                    Task completed = await Task.WhenAny(a, b, watchdog);
                    if (completed.IsFaulted) {
                        // One direction faulted (e.g. connection reset on tunnel teardown): cancel the sibling so the
                        // two open sockets close promptly instead of hanging until the peer closes or the app exits.
                        cts.Cancel();
                    }
                    await Task.WhenAll(a, b); // both directions must EOF/fault for the connection to end
                } finally {
                    cts.Cancel();   // stop the idle watchdog before its CTS is disposed
                    try { await watchdog; } catch { } // it exits on cancellation; never mask the relay outcome
                }
                LOGGER.Info("gpg-agent bridge connection from {remote} closed ({sent} bytes to agent, {received} bytes to client)",
                    remote, counters.sent, counters.received);
            } catch (Exception e) when (e is not OutOfMemoryException) {
                LOGGER.Warn("gpg-agent bridge relay failed: {message}", e.Message);
            }
        }
    }

    /// <summary>Relays bytes from <paramref name="from"/> to <paramref name="to"/> until EOF, counting bytes and
    /// updating the activity timestamp for the idle watchdog. When <paramref name="auditFirstChunk"/> is set (the
    /// client→agent direction), the first chunk's leading Assuan instruction is logged as it passes, so the audit
    /// never blocks the handshake (issue #9).</summary>
    private static async Task copy(TcpClient from, TcpClient to, CancellationToken ct, RelayCounters counters, bool auditFirstChunk, string? remote) {
        var buffer = new byte[4096];
        bool audited = false;
        try {
            while (true) {
                int read = await from.GetStream().ReadAsync(buffer, ct);
                if (read == 0) {
                    // #7: half-close the send side so the peer sees EOF while the reverse relay stays open.
                    try { to.Client.Shutdown(SocketShutdown.Send); } catch { }
                    return;
                }
                Volatile.Write(ref counters.lastActivityTicks, Environment.TickCount64);
                if (auditFirstChunk && !audited) {
                    audited = true;
                    Volatile.Write(ref counters.established, true); // the client spoke → live session, end probe protection
                    LOGGER.Info("gpg-agent bridge accepted connection from {remote}: first instruction {instruction}, first chunk {bytes} bytes",
                        remote, extractInstruction(buffer, read), read);
                }
                await to.GetStream().WriteAsync(buffer.AsMemory(0, read), ct);
                if (auditFirstChunk) {
                    counters.sent += read;
                } else {
                    counters.received += read;
                }
            }
        } catch (OperationCanceledException) {
        }
    }

    /// <summary>Extracts the leading whitespace-delimited token (the Assuan instruction, e.g. SIGN or HAVEKEY) from a
    /// chunk, capped at 32 bytes.</summary>
    internal static string extractInstruction(byte[] chunk, int length) {
        int tokenLength = 0;
        while (tokenLength < length && tokenLength < 32) {
            byte b = chunk[tokenLength];
            if (b is (byte) ' ' or (byte) '\t' or (byte) '\r' or (byte) '\n') {
                break;
            }
            tokenLength++;
        }
        return tokenLength > 0 ? Encoding.UTF8.GetString(chunk, 0, tokenLength) : "?";
    }

    /// <summary>Closes a connection that accepted but whose client never sent anything within <see cref="IDLE_TIMEOUT"/>
    /// — a probe or a dead tunnel cannot pin a relay task forever. Once the client's first data has been relayed
    /// (<see cref="RelayCounters.established"/>), the watchdog stops: the connection is a live agent session, and a
    /// slow card touch or PIN prompt during signing must not be interrupted.</summary>
    private static async Task idleWatchdog(RelayCounters counters, CancellationTokenSource cts, string? remote) {
        try {
            while (!cts.IsCancellationRequested) {
                await Task.Delay(1000, cts.Token);
                if (Volatile.Read(ref counters.established)) {
                    return; // live session; the agent/peer will close it when the operation finishes
                }
                if (Environment.TickCount64 - Volatile.Read(ref counters.lastActivityTicks) >= idleTimeoutMillis) {
                    LOGGER.Warn("gpg-agent bridge closed connection from {remote} after {seconds} s idle before the client sent anything",
                        remote, (int) IDLE_TIMEOUT.TotalSeconds);
                    cts.Cancel();
                    return;
                }
            }
        } catch (OperationCanceledException) {
            // connection closed normally; the watchdog exits
        }
    }

    /// <summary>Per-connection byte counters and the last-activity timestamp shared by the relay copies and the idle
    /// watchdog.</summary>
    private sealed class RelayCounters {
        public long sent;                 // bytes relayed client → agent
        public long received;             // bytes relayed agent → client
        public bool established;          // set once the client's first data is relayed (ends probe protection)
        public long lastActivityTicks = Environment.TickCount64; // written/read via Volatile
    }

    /// <summary>Test seam: when set, overrides the gpg-agent endpoint lookup so relay tests need no real gpg-agent.</summary>
    internal static Func<(int port, byte[] nonce)?>? endpointOverride;

    private static (int port, byte[] nonce)? getSocketEndpoint() {
        if (endpointOverride is { } provider) {
            return provider();
        }
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
