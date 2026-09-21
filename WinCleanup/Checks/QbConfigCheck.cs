using Microsoft.Win32;
using WinCleanup.Cleaners;
using WinCleanup.Utils;

namespace WinCleanup.Checks;

// QuickBooks configuration check: versions, services (via sc), company files,
// .nd/.tlg pairing, ADR backups, entitlement/license files (presence only, never touched).
public static class QbConfigCheck
{
    public static void Run(Logger log, List<QuickBooksCleaner.Install> installs)
    {
        log.Info("=== QUICKBOOKS CONFIG CHECK ===");
        if (installs.Count == 0) { log.Info("no QuickBooks installs found — nothing to check"); return; }

        foreach (var i in installs)
            log.Info($"install: {i.Version} prog={i.ProgramDir} data={i.DataDir}");

        foreach (var svc in new[] { "QBDBMgr", "QBDBMgrN", "QBCFMonitorService", "QBUpdateService" })
        {
            if (!OperatingSystem.IsWindows()) { log.Verbose($"service {svc}: skipped non-Windows"); continue; }
            var out_ = SysProbe.Run("sc", $"query {svc}", log: log);
            string state = out_.Contains("RUNNING") ? "RUNNING" : out_.Contains("STOPPED") ? "STOPPED" :
                string.IsNullOrWhiteSpace(out_) ? "not installed" : "unknown";
            log.Info($"service {svc}: {state}");
        }

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var roots = new List<string>();
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            foreach (var cand in new[] { Path.Combine(d.Name, "Users"), Path.Combine(d.Name, "QB"), Path.Combine(d.Name, "QuickBooks") })
                if (Directory.Exists(cand)) roots.Add(cand);
        roots.Add(Path.Combine(programData, "Intuit"));

        int files = 0;
        foreach (var root in roots.Distinct())
        {
            string[] qbws;
            try { qbws = Directory.GetFiles(root, "*.qbw", SearchOption.AllDirectories); }
            catch (Exception ex) { log.Verbose($"qbw scan skip {root}: {ex.Message}"); continue; }
            foreach (var qbw in qbws.Take(50))
            {
                files++;
                var base_ = qbw[..^".qbw".Length];
                bool nd = File.Exists(base_ + ".nd"), tlg = File.Exists(base_ + ".qbw.tlg");
                var sizeGb = new FileInfo(qbw).Length / 1e9;
                log.Verbose($"company size={sizeGb:F2}GB nd={nd} tlg={tlg} {qbw}");
                log.Info($"company {qbw} ({sizeGb:F2}GB) .nd={nd} .tlg={tlg}" +
                    ((!nd || !tlg) ? " <-- multi-user may fail; rescan with DB Server Manager" : "") +
                    (sizeGb > 1.5 ? " <-- large file; consider condense/verify" : ""));
                var adr = Path.Combine(Path.GetDirectoryName(qbw)!, "Auto Data Recovery");
                if (Directory.Exists(adr))
                {
                    int total = 0, olds = 0;
                    try { total = Directory.GetFiles(adr).Length; olds = Directory.GetFiles(adr, "*.ADR.old").Length; } catch { }
                    log.Info($"  ADR dir: {total} files ({olds} stale .old)");
                }
            }
        }
        if (files == 0) log.Info("no *.qbw company files found under scanned roots");
        log.Info("license: presence check only (never modifies) — " +
            $"EntitlementDataStore.ecml exists={File.Exists(Path.Combine(programData, "Intuit", "Entitlement Client", "v8", "EntitlementDataStore.ecml"))}");
    }
}
