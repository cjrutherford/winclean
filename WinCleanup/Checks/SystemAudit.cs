using WinCleanup.Utils;

namespace WinCleanup.Checks;

// Full system audit with ZERO NuGet deps: BCL + in-box tools (sc/wevtutil/typeperf/ping).
// Every probe is verbose-logged; failures degrade to "n/a" instead of crashing.
public static class SystemAudit
{
    public static void Run(Logger log)
    {
        log.Info("=== SYSTEM AUDIT ===");
        Disk(log); Memory(log); Cpu(log); Smart(log);
        EventErrors(log); StartupApps(log); WindowsUpdate(log); Network(log);
    }

    public static void Disk(Logger log)
    {
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady
            && (d.DriveType == DriveType.Fixed || OperatingSystem.IsWindows())
            && !d.Name.StartsWith("/snap", StringComparison.Ordinal)))
        {
            double pct = d.TotalSize > 0 ? 100.0 * d.AvailableFreeSpace / d.TotalSize : 0;
            string flag = pct < 10 ? " <-- BOTTLENECK: low disk" : pct < 20 ? " <-- warn" : "";
            log.Verbose($"DISK {d.Name} type={d.DriveType} fmt={d.DriveFormat} free={d.AvailableFreeSpace} total={d.TotalSize}");
            log.Info($"disk {d.Name} {d.DriveFormat}: free {d.AvailableFreeSpace / 1e9:F1}GB ({pct:F0}%) of {d.TotalSize / 1e9:F0}GB{flag}");
        }
    }

    public static void Memory(Logger log)
    {
        try
        {
            // GC info is cross-platform and always available; on Windows we add systemwide view.
            var gcGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1e9;
            log.Verbose($"GC TotalAvailableMemory={gcGb:F1}GB procs={Environment.ProcessorCount}");
            if (OperatingSystem.IsWindows())
            {
                var out_ = SysProbe.Run("wmic", "OS get FreePhysicalMemory,TotalVisibleMemorySize /value", log: log);
                var freeKb = ParseWmicValue(out_, "FreePhysicalMemory");
                var totalKb = ParseWmicValue(out_, "TotalVisibleMemorySize");
                if (totalKb > 0)
                {
                    double pct = 100.0 * freeKb / totalKb;
                    log.Info($"ram: {freeKb / 1e6:F1}GB avail of {totalKb / 1e6:F1}GB ({pct:F0}% free){(pct < 15 ? " <-- BOTTLENECK: memory pressure" : "")}");
                    return;
                }
            }
            log.Info($"ram: {gcGb:F1}GB addressable (GC view; exact free needs Windows wmic){(gcGb < 4 ? " <-- warn: low-memory box?" : "")}");
        }
        catch (Exception ex) { log.Info($"ram: n/a ({ex.Message})"); }
    }

    static long ParseWmicValue(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(t[(key.Length + 1)..].Trim(), out var v)) return v;
        }
        return 0;
    }

    public static void Cpu(Logger log)
    {
        log.Info($"cpu: {Environment.ProcessorCount} logical processors; 64-bit={Environment.Is64BitOperatingSystem}");
        if (!OperatingSystem.IsWindows()) { log.Verbose("cpu load probe skipped non-Windows"); return; }
        try
        {
            var out_ = SysProbe.Run("wmic", "cpu get loadpercentage /value", log: log);
            var v = ParseWmicValue(out_, "LoadPercentage");
            log.Info($"cpu load: {v}%{(v > 85 ? " <-- BOTTLENECK" : "")}");
        }
        catch (Exception ex) { log.Info($"cpu load: n/a ({ex.Message})"); }
    }

    public static void Smart(Logger log)
    {
        if (!OperatingSystem.IsWindows()) { log.Info("smart: n/a (non-Windows)"); return; }
        var out_ = SysProbe.Run("wmic", "diskdrive get model,status /format:list", log: log);
        if (string.IsNullOrWhiteSpace(out_)) { log.Info("smart: no WMI data (VM/RAID?)"); return; }
        foreach (var line in out_.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            log.Verbose($"smart: {line}");
        bool bad = out_.Contains("Pred Fail", StringComparison.OrdinalIgnoreCase) ||
                   out_.Contains("Bad", StringComparison.OrdinalIgnoreCase);
        log.Info($"smart wmic status: {(bad ? "CHECK DRIVE <-- REPLACE?" : "OK/predictors absent (see verbose)")}");
    }

    public static void EventErrors(Logger log)
    {
        if (!OperatingSystem.IsWindows()) { log.Info("eventlog: n/a (non-Windows)"); return; }
        foreach (var name in new[] { "System", "Application" })
        {
            // wevtutil: fast count of last-7d errors/warnings without EventLog package.
            var qErr = $"qe-{name} /q:\"*[System[(Level=2) and TimeCreated[timediff(@SystemTime) <= 604800000]]]\" /c /f:text";
            var qWarn = qErr.Replace("Level=2", "Level=3");
            int errs = CountLines(SysProbe.Run("wevtutil", qErr, log: log));
            int warns = CountLines(SysProbe.Run("wevtutil", qWarn, log: log));
            log.Info($"eventlog {name} (7d): ~{errs} errors, ~{warns} warnings{(errs > 50 ? " <-- investigate" : "")}");
        }
    }

    static int CountLines(string s) => string.IsNullOrWhiteSpace(s) ? 0 :
        s.Split('\n').Count(l => l.TrimStart().StartsWith("Event[", StringComparison.Ordinal));

    public static void StartupApps(Logger log)
    {
        var entries = SoftwareInventory.GetStartupEntries(log);
        log.Info($"startup: {entries.Count} entries (Run keys + Startup folders){(entries.Count > 15 ? " <-- review boot impact" : "")}");
        foreach (var e in entries.Take(50)) log.Verbose($"startup: [{e.Scope}] {e.Name} = {e.Command}");
    }

    public static void WindowsUpdate(Logger log)
    {
        if (!OperatingSystem.IsWindows()) { log.Info("windows update: n/a (non-Windows)"); return; }
        var sc = SysProbe.Run("sc", "query wuauserv", log: log);
        log.Verbose($"sc wuauserv: {sc.Trim()}");
        log.Info($"windows update service: {(sc.Contains("RUNNING") ? "RUNNING" : sc.Contains("STOPPED") ? "STOPPED" : "unknown")}");
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            log.Info(k == null ? "windows update: no pending reboot" : "windows update: REBOOT PENDING");
        }
        catch (Exception ex) { log.Verbose($"reboot-required check: {ex.Message}"); }
    }

    public static void Network(Logger log)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var p = new System.Net.NetworkInformation.Ping();
            var r = p.Send("8.8.8.8", 2000);
            sw.Stop();
            log.Verbose($"ping status={r.Status} ms={sw.ElapsedMilliseconds}");
            log.Info($"net ping 8.8.8.8: {r.Status} {sw.ElapsedMilliseconds}ms{(sw.ElapsedMilliseconds > 200 ? " <-- slow link?" : "")}");
        }
        catch (Exception ex) { log.Info($"net: n/a ({ex.Message})"); }
    }
}
