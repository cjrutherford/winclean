using WinCleanup.Utils;

namespace WinCleanup.Checks;

// Correlates disk/RAM/CPU + software/config inventory into a prioritized,
// non-destructive action list. Principle: keep everything intact — tune, don't remove.
// Only temp/cache/orphans are ever deleted; everything else is disable/review guidance.
public static class BottleneckReport
{
    public static void Run(Logger log, string fixtureRoot = "")
    {
        log.Info("=== BOTTLENECK ANALYSIS (keep-everything-intact) ===");
        int findings = 0;
        void Finding(string s) { findings++; log.Info($"  [{findings}] {s}"); }

        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed
            && d.TotalSize > 2L * 1024 * 1024 * 1024 && !d.Name.StartsWith("/snap", StringComparison.Ordinal)))
        {
            double pct = d.TotalSize > 0 ? 100.0 * d.AvailableFreeSpace / d.TotalSize : 100;
            log.Verbose($"bottleneck disk {d.Name} freePct={pct:F1}");
            if (pct < 10) Finding($"DISK {d.Name} critically full ({pct:F0}% free): empty Recycle Bin, run --clean, move company-file backups off C:, enable Storage Sense. No app uninstall needed.");
            else if (pct < 20) Finding($"DISK {d.Name} getting full ({pct:F0}% free): schedule monthly --clean + review Downloads.");
        }

        var startup = SoftwareInventory.GetStartupEntries(log);
        if (startup.Count > 15) Finding($"BOOT: {startup.Count} startup entries slow login — Task Manager > Startup apps > Disable non-essentials (keep QB DB Server Manager, AV). Nothing uninstalled.");
        else if (startup.Count > 0) log.Verbose($"boot ok: {startup.Count} startup entries");
        var tasks = SoftwareInventory.GetScheduledTasks(log);
        var updaters = tasks.Where(t => t.Name.Contains("Update", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Adobe", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Google", StringComparison.OrdinalIgnoreCase)).ToList();
        if (updaters.Count > 5) Finding($"BACKGROUND: {updaters.Count} updater tasks ({string.Join(", ", updaters.Take(3).Select(t => t.Name.Split('\\').Last()))}…) — stagger schedules; don't disable security/AV updaters.");

        var apps = SoftwareInventory.GetInstalledApps(log, fixtureRoot);
        var dupes = apps.GroupBy(a => a.Name.Split(' ')[0], StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 2).Take(3).ToList();
        foreach (var g in dupes) log.Verbose($"possible dupes: {g.Key} x{g.Count()}");
        if (dupes.Count > 0) Finding("CLUTTER: multiple versions/side-by-side installs detected (see verbose) — review in Add/Remove, keep current + QB-required runtimes (.NET, VC++, SQL). When in doubt, keep.");
        var browsers = apps.Where(a => a.Name.Contains("Chrome", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("Edge", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("Firefox", StringComparison.OrdinalIgnoreCase)).ToList();
        if (browsers.Count >= 2) Finding("BROWSER CACHE: 2+ browsers installed — clearing their caches (safe, regenerable) often frees GBs without touching data.");

        log.Info("Safe performance wins (no data loss): 1) --clean temp/cache 2) disable unneeded startup 3) Storage Sense on 4) reboot after updates 5) QB: verify/rebuild + condense if .qbw>1.5GB 6) browser cache clear 7) move .qbb backups to external.");
        log.Info($"bottleneck exit findings={findings} (details in verbose log)");
    }
}
