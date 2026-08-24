using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PasskeyPick.Tests;

/// <summary>Loopback integration tests for the GPG bridge relay: a fake gpg-agent (a plain TcpListener) stands in for
/// the real agent via <see cref="GpgBridge.endpointOverride"/>, so no Gpg4win installation is needed.</summary>
public class GpgBridgeTests {

    private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Relay_PerformsNonceHandshakeAndRelaysBothDirections() {
        byte[] nonce = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        var    agentGotNonce   = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var    agentGotCommand = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var agentListener = startFakeAgent(async stream => {
            byte[] receivedNonce = new byte[16];
            await readExact(stream, receivedNonce);
            agentGotNonce.SetResult(receivedNonce);
            await stream.WriteAsync("OK Pleased to meet you\r\n"u8.ToArray());
            agentGotCommand.SetResult(await readLine(stream));
            await stream.WriteAsync("OK all done\r\n"u8.ToArray());
            await Task.Delay(200); // let the reply flush through the relay before this socket closes
        });

        int bridgePort = freePort();
        GpgBridge.endpointOverride = () => (agentPort(agentListener), nonce);
        GpgBridge.start(bridgePort);
        try {
            Assert.True(GpgBridge.isRunning);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, bridgePort);
            NetworkStream stream = client.GetStream();

            Assert.Equal("OK Pleased to meet you", await readLine(stream)); // agent → client relay, no client input needed
            await stream.WriteAsync("HAVEKEY ABCD\r\n"u8.ToArray());
            Assert.Equal("OK all done", await readLine(stream));            // client → agent → client

            Assert.Equal(nonce, await agentGotNonce.Task.WaitAsync(TIMEOUT));
            Assert.Equal("HAVEKEY ABCD", await agentGotCommand.Task.WaitAsync(TIMEOUT));
        } finally {
            GpgBridge.stop();
            GpgBridge.endpointOverride = null;
        }
        Assert.False(GpgBridge.isRunning);
    }

    [Fact]
    public async Task Relay_HalfCloseKeepsTheReverseDirectionOpen() {
        if (!await halfCloseWorks()) {
            return; // a filter driver / security product on this host aborts half-closed connections entirely
        }
        byte[] nonce = new byte[16];
        var    agentSawEof = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var    agentError  = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var agentListener = startFakeAgent(async stream => {
            await readExact(stream, new byte[16]);
            await stream.WriteAsync("OK\r\n"u8.ToArray());
            await readLine(stream); // the client's instruction
            agentSawEof.SetResult(await stream.ReadAsync(new byte[1])); // client half-closed → 0 (EOF)
            await stream.WriteAsync("OK final\r\n"u8.ToArray());        // ...but the agent can still answer
            await Task.Delay(200);
        }, agentError);

        int bridgePort = freePort();
        GpgBridge.endpointOverride = () => (agentPort(agentListener), nonce);
        GpgBridge.start(bridgePort);
        try {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, bridgePort);
            NetworkStream stream = client.GetStream();

            Assert.Equal("OK", await readLine(stream));
            await stream.WriteAsync("SIGN something\r\n"u8.ToArray());
            client.Client.Shutdown(SocketShutdown.Send); // half-close: no more client data

            Task seen = await Task.WhenAny(agentSawEof.Task, agentError.Task);
            if (seen == agentError.Task) {
                throw new InvalidOperationException("The fake gpg-agent failed", await agentError.Task);
            }
            Assert.Equal(0, await agentSawEof.Task.WaitAsync(TIMEOUT));
            Assert.Equal("OK final", await readLine(stream)); // reverse direction survived the half-close
        } finally {
            GpgBridge.stop();
            GpgBridge.endpointOverride = null;
        }
    }

    [Fact]
    public async Task Relay_SilentClientIsClosedByTheIdleWatchdog() {
        byte[] nonce = new byte[16];
        TimeSpan original = GpgBridge.IDLE_TIMEOUT;
        GpgBridge.IDLE_TIMEOUT = TimeSpan.FromMilliseconds(200); // the watchdog polls once a second

        using var agentListener = startFakeAgent(async stream => {
            await readExact(stream, new byte[16]);
            await stream.WriteAsync("OK\r\n"u8.ToArray());
            // The client never speaks; just stay open until the bridge tears the connection down.
#pragma warning disable CA2022 // any single read (or the teardown's EOF) ends the wait — exactly the intent
            await stream.ReadAsync(new byte[1]);
#pragma warning restore CA2022
        });

        int bridgePort = freePort();
        GpgBridge.endpointOverride = () => (agentPort(agentListener), nonce);
        GpgBridge.start(bridgePort);
        try {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, bridgePort);
            NetworkStream stream = client.GetStream();
            Assert.Equal("OK", await readLine(stream));

            // No client data: within (poll interval + timeout) the watchdog cancels the relay and both sockets close.
            // The teardown surfaces as a clean EOF or a connection reset, depending on the TCP stack.
            var read = stream.ReadAsync(new byte[1]).AsTask();
            Task finished = await Task.WhenAny(read, Task.Delay(TIMEOUT));
            Assert.Same(read, finished);
            try {
                Assert.Equal(0, await read);
            } catch (IOException) {
                // reset instead of FIN — still proof that the watchdog closed the connection
            }
        } finally {
            GpgBridge.stop();
            GpgBridge.endpointOverride = null;
            GpgBridge.IDLE_TIMEOUT = original;
        }
    }

    [Theory]
    [InlineData("SIGN --hash=sha256\r\n", "SIGN")]
    [InlineData("HAVEKEY ABCD\r\n", "HAVEKEY")]
    [InlineData(" leading-whitespace", "?")]
    [InlineData("", "?")]
    public void ExtractInstruction_ReadsTheLeadingToken(string chunk, string expected) {
        byte[] bytes = Encoding.UTF8.GetBytes(chunk);
        Assert.Equal(expected, GpgBridge.extractInstruction(bytes, bytes.Length));
    }

    [Fact]
    public void ExtractInstruction_IsCappedAt32Bytes() {
        byte[] bytes = Encoding.UTF8.GetBytes(new string('A', 40));
        Assert.Equal(new string('A', 32), GpgBridge.extractInstruction(bytes, bytes.Length));
    }

    /// <summary>Starts a loopback listener that hands the accepted connection's stream to <paramref name="handle"/>.
    /// Handler failures are recorded in <paramref name="agentError"/> instead of being swallowed.</summary>
    private static TcpListener startFakeAgent(Func<NetworkStream, Task> handle, TaskCompletionSource<Exception>? agentError = null) {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(async () => {
            try {
                using TcpClient agent = await listener.AcceptTcpClientAsync();
                await handle(agent.GetStream());
            } catch (Exception e) {
                agentError?.TrySetResult(e);
                // listener stopped or relay torn down mid-test; the assertions decide the outcome
            }
        });
        return listener;
    }

    /// <summary>Some hosts (filter drivers, security software — observed as WSAECONNABORTED 10053, "software on your
    /// host machine aborted the connection") tear down a loopback connection entirely when one side calls
    /// Shutdown(Send), so a half-close cannot be observed or relayed there. Probe once: after the client's Shutdown,
    /// the server must still see EOF and the client must still receive the server's answer.</summary>
    private static async Task<bool> halfCloseWorks() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint) listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () => {
            try {
                using TcpClient server = await listener.AcceptTcpClientAsync();
                NetworkStream stream = server.GetStream();
                await readExact(stream, new byte[1]);
                Assert.Equal(0, await stream.ReadAsync(new byte[1])); // the client's FIN must arrive as EOF
                await stream.WriteAsync(new byte[1]);                 // and the reverse direction must still work
                await Task.Delay(100);
                return true;
            } catch {
                return false;
            }
        });
        try {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(new byte[1]);
            client.Client.Shutdown(SocketShutdown.Send);
            Assert.Equal(1, await stream.ReadAsync(new byte[1]).AsTask().WaitAsync(TIMEOUT));
            return await serverTask;
        } catch {
            return false;
        }
    }

    private static int agentPort(TcpListener listener) => ((IPEndPoint) listener.LocalEndpoint).Port;

    private static int freePort() {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint) probe.LocalEndpoint).Port;
    }

    private static async Task readExact(NetworkStream stream, byte[] buffer) {
        int offset = 0;
        while (offset < buffer.Length) {
            int read = await stream.ReadAsync(buffer.AsMemory(offset)).AsTask().WaitAsync(TIMEOUT);
            Assert.NotEqual(0, read);
            offset += read;
        }
    }

    private static async Task<string> readLine(NetworkStream stream) {
        var line = new List<byte>();
        var  one  = new byte[1];
        while (true) {
            int read = await stream.ReadAsync(one).AsTask().WaitAsync(TIMEOUT);
            Assert.NotEqual(0, read);
            if (one[0] == (byte) '\n') {
                return Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
            }
            line.Add(one[0]);
        }
    }

}
