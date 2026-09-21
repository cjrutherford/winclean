using WinCleanup.Models;
using WinCleanup.Utils;

namespace WinCleanup.Cleaners;

// General Windows temp/cache locations. All age-gated + locked-safe.
// FixtureRoot: when set (Linux manual verification), cleaners run against
//   <FixtureRoot>/win/Temp, <FixtureRoot>/win/WindowsTemp, ... instead of real paths.
public static class WindowsCleaner
{
    public static DeleteStats Run(CleanupOptions o, Logger log)
    {
        var s = new DeleteStats();
        var minAge = TimeSpan.FromDays(o.MinAgeDays);
        log.Verbose($"WindowsCleaner start dryRun={o.DryRun} minAge={o.MinAgeDays}d fixture='{o.FixtureRoot}'");

        var targets = ResolveTargets(o, log);

        bool touchedWU = false;
        if (!o.DryRun && string.IsNullOrEmpty(o.FixtureRoot) && OperatingSystem.IsWindows()
            && targets.Any(t => t.Contains("SoftwareDistribution")))
            touchedWU = StopServicePair(log, "wuauserv", "BITS");

        foreach (var t in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (t.EndsWith("Prefetch", StringComparison.OrdinalIgnoreCase) && !o.IncludePrefetch)
            { log.Verbose($"SKIP prefetch-disabled {t}"); log.Info("skip Prefetch (use --include-prefetch to enable)"); continue; }
            SafeDelete.CleanDirAged(t, minAge, o.DryRun, s, log);
        }

        if (touchedWU) StartServicePair(log, "BITS", "wuauserv");
        EmptyRecycleBin(o, log, s);
        log.Verbose($"WindowsCleaner exit deleted={s.FilesDeleted} freed={s.BytesFreed} skipAge={s.FilesSkippedAge} skipLock={s.FilesSkippedLocked} errors={s.Errors.Count}");
        return s;
    }

    public static List<string> ResolveTargets(CleanupOptions o, Logger log)
    {
        if (!string.IsNullOrEmpty(o.FixtureRoot))
        {
            var f = new List<string>
            {
                Path.Combine(o.FixtureRoot, "win", "Temp"),
                Path.Combine(o.FixtureRoot, "win", "WindowsTemp"),
                Path.Combine(o.FixtureRoot, "win", "SoftwareDistributionDownload"),
            };
            log.Verbose($"fixture targets: {string.Join("; ", f)}");
            return f;
        }
        var targets = new List<string>();
        var tmp = Environment.GetEnvironmentVariable("TEMP");
        if (!string.IsNullOrEmpty(tmp)) targets.Add(tmp);
        var winTmp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        targets.Add(winTmp);
        var usersRoot = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Users");
        if (Directory.Exists(usersRoot))
            foreach (var u in Directory.GetDirectories(usersRoot))
            {
                var t = Path.Combine(u, "AppData", "Local", "Temp");
                if (Directory.Exists(t)) targets.Add(t);
            }
        targets.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"));
        targets.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "DeliveryOptimization", "Cache"));
        targets.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"));
        targets.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "INetCache"));
        targets.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS"));
        var iisLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "..", "inetpub", "logs", "LogFiles");
        targets.Add(Path.GetFullPath(iisLog));
        log.Verbose($"resolved {targets.Count} real targets");
        foreach (var t in targets) log.Verbose($"TARGET {(Directory.Exists(t) ? "exists" : "missing")} {t}");
        return targets;
    }

    static bool StopServicePair(Logger log, params string[] names)
    {
        // sc/stop via in-box tool (no ServiceController package).
        bool ok = true;
        foreach (var n in names)
        {
            var out_ = SysProbe.Run("sc", $"stop {n}", log: log);
            log.Verbose($"sc stop {n}: {out_.Trim()}");
            if (out_.Contains("FAILED", StringComparison.OrdinalIgnoreCase)) ok = false;
        }
        Thread.Sleep(2000);
        return ok;
    }

    static void StartServicePair(Logger log, params string[] names)
    {
        foreach (var n in names)
        {
            var out_ = SysProbe.Run("sc", $"start {n}", log: log);
            log.Verbose($"sc start {n}: {out_.Trim()}");
        }
    }

    static void EmptyRecycleBin(CleanupOptions o, Logger log, DeleteStats s)
    {
        if (!string.IsNullOrEmpty(o.FixtureRoot)) { log.Verbose("SKIP recycle-bin under fixture"); return; }
        if (!OperatingSystem.IsWindows()) { log.Verbose("SKIP recycle-bin non-Windows"); return; }
        if (o.DryRun) { log.Info("[dry-run] empty Recycle Bin (SHEmptyRecycleBin)"); return; }
        try
        {
            int r = SHEmptyRecycleBin(IntPtr.Zero, null, 7);
            log.Verbose($"RecycleBin HRESULT={r}");
            if (r != 0) log.Warn($"Recycle Bin returned HRESULT {r}");
        }
        catch (Exception ex) { s.Errors.Add($"RecycleBin: {ex.Message}"); }
    }

    [System.Runtime.InteropServices.DllImport("Shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, int flags);
}
