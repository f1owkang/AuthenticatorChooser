using System.Diagnostics;
using System.Text;

namespace PasskeyPick;

/// <summary>Generates a copyable plain-text GPG diagnostics report (issue #8): gpg binary path/version, gpg-agent
/// process path (detect MSYS shadowing), gpgconf socket paths + existence, SSH_AUTH_SOCK, ssh-add -L,
/// gpg --card-status, and the OpenSSH Authentication Agent service state. Public data only — never secrets.</summary>
internal static class GpgDiagnostics {

    private const string OPENSSH_SERVICE_NAME = "ssh-agent";

    public static string generate() {
        var sb = new StringBuilder();

        string? where   = GpgTools.resolveSystemBinary("where.exe");
        string? gpg     = GpgTools.resolveKnownBinary("gpg.exe");
        string? gpgconf = GpgTools.resolveKnownBinary("gpgconf.exe");
        string? sshAdd  = GpgTools.resolveOpenSshBinary("ssh-add.exe");
        string? sc      = GpgTools.resolveSystemBinary("sc.exe");

        sb.AppendLine("[gpg version]");
        sb.AppendLine(run(where, "gpg"));
        sb.AppendLine(run(gpg, "--version").Split('\n').FirstOrDefault()?.Trim());

        sb.AppendLine();
        sb.AppendLine("[gpg-agent process]");
        string[] agentProcesses = Process.GetProcessesByName("gpg-agent").Select(p => {
            try { return p.MainModule?.FileName ?? p.ProcessName; }
            catch { return $"{p.ProcessName} (path not readable)"; }
        }).ToArray();
        sb.AppendLine(agentProcesses.Length == 0 ? "not running" : string.Join(Environment.NewLine, agentProcesses));

        sb.AppendLine();
        sb.AppendLine("[gpgconf socket dirs]");
        string sockets = run(gpgconf, "--list-dirs agent-socket agent-extra-socket agent-ssh-socket");
        if (string.IsNullOrWhiteSpace(sockets)) {
            sb.AppendLine("(gpgconf --list-dirs failed)");
        } else {
            foreach (string line in sockets.Split('\n')) {
                string path = line.Trim();
                if (path.Length == 0) {
                    continue;
                }
                sb.AppendLine($"{path}  exists={File.Exists(path)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("[SSH_AUTH_SOCK]");
        sb.AppendLine(Environment.GetEnvironmentVariable("SSH_AUTH_SOCK") ?? "(not set)");

        sb.AppendLine();
        sb.AppendLine("[ssh-add -L]");
        sb.AppendLine(run(sshAdd, "-L"));

        sb.AppendLine();
        sb.AppendLine("[gpg --card-status]");
        sb.AppendLine(run(gpg, "--card-status"));

        sb.AppendLine();
        sb.AppendLine("[OpenSSH Authentication Agent service]");
        sb.AppendLine(run(sc, $"query {OPENSSH_SERVICE_NAME}"));

        return sb.ToString();
    }

    /// <summary>Runs <paramref name="exe"/> (an absolute path) with the given arguments, or reports when the binary is
    /// not installed. Every binary is resolved to a trusted absolute path before launch — this process runs elevated,
    /// so a bare filename resolved through PATH could execute a planted binary.</summary>
    private static string run(string? exe, string arguments) =>
        exe is null ? "(binary not found)" : GpgTools.runCommand(exe, arguments);
}
