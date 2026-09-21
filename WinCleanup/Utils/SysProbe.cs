using System.Diagnostics;

namespace WinCleanup.Utils;

// Zero-dependency OS probing: shell out to in-box Windows tools with timeouts.
// Returns "" when unavailable (e.g. Linux manual-verification runs).
public static class SysProbe
{
    public static string Run(string file, string args, int timeoutMs = 15000, Logger? log = null)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } log?.Verbose($"PROBE timeout {file} {args}"); return ""; }
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            log?.Verbose($"PROBE {file} {args} exit={p.ExitCode} out={stdout.Length}B err={Trunc(stderr, 200)}");
            return stdout;
        }
        catch (Exception ex) { log?.Verbose($"PROBE fail {file} {args}: {ex.Message}"); return ""; }
    }

    // PowerShell runner for Tier-0 mitigations and Appx queries.
    // Windows only; returns "" elsewhere. Every invocation is verbose-logged.
    public static string PowerShell(string command, int timeoutMs = 30000, Logger? log = null)
    {
        if (!OperatingSystem.IsWindows()) { log?.Verbose("PROBE powershell skipped non-Windows"); return ""; }
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
        return Run("powershell", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", timeoutMs, log);
    }

    static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
