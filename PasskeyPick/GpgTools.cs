using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32.TaskScheduler;

namespace PasskeyPick;

/// <summary>Locates the Gpg4win build of GnuPG, reads the gpg-agent "extra socket" file (a simulated UDS: a plain file
/// holding a TCP port + nonce), and runs gpg-connect-agent. All methods are defensive: never throw, log instead
/// (issue #7, #8). External binaries are only ever launched from trusted absolute paths (Gpg4win dirs / System32 /
/// System32\OpenSSH) — never a bare filename resolved through PATH — because this process runs elevated.</summary>
internal static class GpgTools {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(GpgTools).FullName!);

    private static readonly string[] KNOWN_BIN_DIRS = [
        @"C:\Program Files\GnuPG\bin",
        @"C:\Program Files (x86)\GnuPG\bin"
    ];

    /// <summary>Absolute path to the Gpg4win gpg-connect-agent.exe, or null. Only the trusted Program Files install
    /// directories are searched — never PATH — because a lower-privileged user who can write an earlier PATH entry
    /// could plant a malicious binary for this elevated process to run.</summary>
    public static string? resolveGpg4winConnectAgent() {
        foreach (string dir in KNOWN_BIN_DIRS) {
            string candidate = Path.Combine(dir, "gpg-connect-agent.exe");
            if (File.Exists(candidate)) {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>Absolute path to <paramref name="exeName"/> under the trusted Gpg4win install directories, or null.
    /// Like <see cref="resolveGpg4winConnectAgent"/>, never falls back to PATH.</summary>
    public static string? resolveKnownBinary(string exeName) {
        foreach (string dir in KNOWN_BIN_DIRS) {
            string candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate)) {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>Absolute path to a Windows system tool (<c>where.exe</c>, <c>sc.exe</c>) under System32, or null.</summary>
    public static string? resolveSystemBinary(string exeName) {
        string candidate = Path.Combine(Environment.SystemDirectory, exeName);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Absolute path to a client binary of the inbox OpenSSH installation (<c>ssh-add.exe</c>), or null.</summary>
    public static string? resolveOpenSshBinary(string exeName) {
        string candidate = Path.Combine(Environment.SystemDirectory, "OpenSSH", exeName);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Starts the gpg-agent via `gpg-connect-agent /bye` (Gpg4win absolute path only). Returns whether the
    /// command succeeded. When this process is elevated, the agent is started at medium integrity (issue #10) so its
    /// openssh-ssh-agent named pipe stays reachable from normal, non-elevated terminals.</summary>
    public static bool startAgent() {
        string? fileName = resolveGpg4winConnectAgent();
        if (fileName is null) {
            LOGGER.Warn("gpg-connect-agent not found under {dirs}; cannot start gpg-agent", string.Join(", ", KNOWN_BIN_DIRS));
            return false;
        }
        // #10: an elevated parent would spawn a high-integrity agent, whose named pipe is denied to medium-integrity
        // terminals (UIPI). A limited scheduled task gives the agent medium integrity instead. Fall back to a plain
        // elevated launch if the task route fails, so the bridge and keep-alive still work (only normal-terminal SSH
        // would then be affected).
        if (isElevated) {
            if (startAgentAtMediumIntegrity(fileName)) {
                return true;
            }
            LOGGER.Warn("Medium-integrity gpg-agent launch failed; falling back to an elevated launch (normal terminals will not see the ssh-agent pipe)");
        }
        return startAgentDirect(fileName);
    }

    /// <summary>Launches <paramref name="exePath"/> /bye directly in the current (same-integrity) context.</summary>
    private static bool startAgentDirect(string fileName) {
        try {
            using var p = new Process {
                StartInfo = new ProcessStartInfo(fileName, "/bye") {
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                }
            };
            p.Start();
            if (!p.WaitForExit(5_000)) {
                try { p.Kill(); } catch { }
                LOGGER.Warn("gpg-connect-agent /bye timed out");
                return false;
            }
            if (p.ExitCode != 0) {
                LOGGER.Warn("gpg-connect-agent /bye exited with {exitCode}: {stderr}", p.ExitCode, p.StandardError.ReadToEnd().Trim());
                return false;
            }
            LOGGER.Debug("gpg-agent started via {fileName}", fileName);
            return true;
        } catch (Exception e) when (e is not OutOfMemoryException) {
            LOGGER.Warn("Could not start gpg-agent via {fileName}: {message}", fileName, e.Message);
            return false;
        }
    }

    /// <summary>Whether this process runs elevated (high integrity); only then does the agent need downgrading.</summary>
    private static bool isElevated {
        get {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Serializes the one-shot scheduled-task launch: the bridge, keep-alive, and settings dialog can call
    /// <see cref="startAgent"/> concurrently, and the TaskService singleton is not documented thread-safe.</summary>
    private static readonly object taskLaunchSync = new();

    /// <summary>Launches <paramref name="exePath"/> /bye at medium integrity via a one-shot limited scheduled task, so
    /// the gpg-agent it starts creates its openssh-ssh-agent named pipe at medium integrity — reachable from normal,
    /// non-elevated terminals (issue #10). The task name is per-user (SID), (re)registered, run to completion, and
    /// deleted. Returns whether the launch was submitted.</summary>
    private static bool startAgentAtMediumIntegrity(string exePath) {
        try {
            string domainAndUsername = $@"{Environment.UserDomainName}\{Environment.UserName}";
            string userSid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            string taskName = $"{Startup.PROGRAM_NAME}_gpg_agent_{userSid}";
            lock (taskLaunchSync) {
                TaskDefinition task = TaskService.Instance.NewTask();
                task.Principal.LogonType = TaskLogonType.InteractiveToken;
                task.Principal.UserId = domainAndUsername;
                task.Principal.RunLevel = TaskRunLevel.LUA; // least-privileged = medium integrity (issue #10)
                task.Settings.Enabled = true;
                task.Actions.Add(exePath, "/bye");
                TaskService.Instance.RootFolder.RegisterTaskDefinition(taskName, task, TaskCreation.CreateOrUpdate, domainAndUsername, null, TaskLogonType.InteractiveToken);
                // Wait for the scheduler to launch the task and for /bye to finish (it spawns the agent and exits
                // promptly), so LastTaskResult is meaningful and deleting the definition cannot race the launch.
                RunningTask running = TaskService.Instance.GetTask(taskName).Run();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline && running.State is TaskState.Queued or TaskState.Running) {
                    Thread.Sleep(50);
                }
                if (running.State is TaskState.Queued or TaskState.Running) {
                    LOGGER.Warn("gpg-agent medium-integrity launch task did not finish in 5 s");
                } else if (running.LastTaskResult != 0) {
                    LOGGER.Warn("gpg-connect-agent /bye (medium integrity) exited with {exitCode}", running.LastTaskResult);
                }
                TaskService.Instance.RootFolder.DeleteTask(taskName, false);
            }
            LOGGER.Debug("gpg-agent started at medium integrity via limited scheduled task (issue #10)");
            return true;
        } catch (Exception e) when (e is not OutOfMemoryException) {
            LOGGER.Warn("Could not start gpg-agent at medium integrity via scheduled task: {message}", e.Message);
            return false;
        }
    }

    /// <summary>Path of the gpg-agent extra-socket file (`gpgconf --list-dir agent-extra-socket`), or null.</summary>
    public static string? getExtraSocketPath() {
        string? gpgconf = resolveKnownBinary("gpgconf.exe");
        if (gpgconf is null) {
            return null;
        }
        string output = runCommand(gpgconf, "--list-dir agent-extra-socket");
        string? line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
    }

    /// <summary>Parses the simulated-UDS file into (port, 16-byte nonce), or null on any malformed content.
    /// Standard variant: decimal port followed by 16 raw nonce bytes. Cygwin variant: `!<socket >PORT s 8HEX-8HEX-8HEX-8HEX`.</summary>
    public static (int port, byte[] nonce)? parseSocketFile(string path) {
        try {
            byte[] buffer = File.ReadAllBytes(path);
            if (buffer.Length < 16) {
                return null;
            }
            if (buffer.AsSpan().StartsWith("!<socket >"u8)) {
                return parseCygwinVariant(buffer[10..]);
            }
            byte[] left = buffer[..^16];
            if (left.Length == 0 || !int.TryParse(Encoding.UTF8.GetString(left).Trim(), out int port) || port is < 1 or > 65535) {
                return null;
            }
            return (port, buffer[^16..]);
        } catch (Exception e) when (e is not OutOfMemoryException) {
            LOGGER.Warn("Could not read gpg-agent socket file {path}: {message}", path, e.Message);
            return null;
        }
    }

    /// <summary>Cygwin variant: `!<socket >&lt;port&gt; s &lt;8hex&gt;-&lt;8hex&gt;-&lt;8hex&gt;-&lt;8hex&gt;`.</summary>
    private static (int port, byte[] nonce)? parseCygwinVariant(ReadOnlySpan<byte> body) {
        string text = Encoding.UTF8.GetString(body).Trim('\0', ' ', '\n', '\r');
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !int.TryParse(parts[0], out int port) || port is < 1 or > 65535) {
            return null;
        }
        string[] words = parts[2].Split('-');
        if (words.Length != 4) {
            return null;
        }
        var nonce = new byte[16];
        for (int i = 0; i < 4; i++) {
            if (words[i].Length != 8 || !uint.TryParse(words[i], System.Globalization.NumberStyles.HexNumber, null, out uint word)) {
                return null;
            }
            // %08x prints the u32 as hex; GnuPG reads the raw bytes back in native (little-endian) order.
            BitConverter.GetBytes(word).CopyTo(nonce, i * 4);
        }
        return (port, nonce);
    }

    /// <summary>Checks whether the gpg-agent is reachable: the extra-socket file must exist and its TCP port must accept
    /// a connect. Used by keep-alive and the settings-dialog status section.</summary>
    public static bool probeAgent() {
        string? socketPath = getExtraSocketPath();
        if (socketPath is null || !File.Exists(socketPath) || parseSocketFile(socketPath) is not { } parsed) {
            return false;
        }
        try {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", parsed.port).Wait(1_000) && client.Connected;
        } catch {
            return false;
        }
    }

    /// <summary>Runs a command, capturing stdout+stderr with a timeout. Never throws; returns an error string on
    /// failure. A 10 s timeout bounds any deadlock risk from sequential stream reads.</summary>
    public static string runCommand(string fileName, string arguments, int timeoutMs = 10_000) {
        try {
            using var p = new Process {
                StartInfo = new ProcessStartInfo(fileName, arguments) {
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                }
            };
            p.Start();
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs)) {
                try { p.Kill(); } catch { }
                return $"(timed out after {timeoutMs} ms)";
            }
            return string.Concat(stdout, stderr).Trim();
        } catch (Exception e) when (e is not OutOfMemoryException) {
            return $"(failed to run {fileName}: {e.Message})";
        }
    }
}
