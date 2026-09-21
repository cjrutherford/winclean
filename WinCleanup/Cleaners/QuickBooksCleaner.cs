using Microsoft.Win32;
using WinCleanup.Models;
using WinCleanup.Utils;

namespace WinCleanup.Cleaners;

// QuickBooks-aware cleaner. Auto-detect, safe-allowlist only.
// SAFE (per Intuit community guidance):
//   ...\Components\QBUpdateCache\*, ...\Components\DownloadQB*\*, ...\SPatch, ...\EPatch
//   QBDataServiceUser*\AppData\Local\Temp\search_data.*.dat (orphaned temp)
//   *.ADR.old (stale auto-recovery), Qbwatch.log / qbupdate.log (regenerable logs)
// NEVER: *.qbw .qbb .qbm .nd .tlg EntitlementDataStore.ecml qbprint.qbp PConfig\Data1.cab
public static class QuickBooksCleaner
{
    public record Install(string Version, string ProgramDir, string DataDir);

    public static List<Install> Detect(Logger? log = null, string fixtureRoot = "")
    {
        if (!string.IsNullOrEmpty(fixtureRoot))
        {
            var f = new List<Install>();
            var qbRoot = Path.Combine(fixtureRoot, "qb");
            if (Directory.Exists(qbRoot))
                foreach (var d in Directory.GetDirectories(qbRoot, "QuickBooks*"))
                {
                    var ver = Path.GetFileName(d);
                    f.Add(new Install(ver, Path.Combine(d, "Program"), Path.Combine(d, "Data")));
                }
            log?.Verbose($"fixture QB detect: {f.Count} installs under {qbRoot}");
            foreach (var i in f) log?.Verbose($"fixture install: {i.Version} prog={i.ProgramDir} data={i.DataDir}");
            return f;
        }
        var found = new List<Install>();
        string[] progRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];
        foreach (var root in progRoots)
        {
            var intuit = Path.Combine(root, "Intuit");
            if (!Directory.Exists(intuit)) { log?.Verbose($"MISS {intuit}"); continue; }
            foreach (var d in Directory.GetDirectories(intuit, "QuickBooks*"))
            {
                found.Add(new Install(Path.GetFileName(d), d, ""));
                log?.Verbose($"found prog {d}");
            }
        }
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (var cand in new[] { Path.Combine(programData, "Intuit"), Path.Combine(programData, "Intuit QuickBooks") })
            if (Directory.Exists(cand))
                foreach (var d in Directory.GetDirectories(cand, "QuickBooks*"))
                {
                    if (!found.Any(f => f.Version == Path.GetFileName(d)))
                    { found.Add(new Install(Path.GetFileName(d), "", d)); log?.Verbose($"found data {d}"); }
                }

        // Registry display versions (Windows only; best effort)
        if (OperatingSystem.IsWindows())
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var k = baseKey.OpenSubKey(@"SOFTWARE\Intuit\QuickBooks"))
                    if (k != null)
                        foreach (var sub in k.GetSubKeyNames())
                        {
                            log?.Verbose($"registry QB key: {sub} [{view}]");
                            if (!found.Any(f => f.Version.Contains(sub)))
                                found.Add(new Install(sub, "", ""));
                        }
        }
        catch (Exception ex) { log?.Verbose($"registry scan skipped: {ex.Message}"); }

        log?.Info(found.Count == 0 ? "QuickBooks: no installs detected" :
            $"QuickBooks detected: {string.Join(", ", found.Select(f => f.Version))}");
        return found.DistinctBy(f => f.Version).ToList();
    }

    public static DeleteStats Run(CleanupOptions o, Logger log, List<Install> installs)
    {
        var s = new DeleteStats();
        var minAge = TimeSpan.FromDays(o.MinAgeDays);
        log.Verbose($"QuickBooksCleaner start installs={installs.Count} dryRun={o.DryRun} minAge={o.MinAgeDays}d");
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        foreach (var inst in installs)
        {
            log.Verbose($"INSTALL {inst.Version} prog={inst.ProgramDir} data={inst.DataDir}");
            foreach (var dataRoot in new[] { inst.DataDir, Path.Combine(programData, "Intuit", inst.Version) }
                         .Where(Directory.Exists).Distinct())
            {
                log.Verbose($"DATAROOT {dataRoot}");
                var comp = Path.Combine(dataRoot, "Components");
                if (Directory.Exists(comp))
                {
                    foreach (var d in Directory.GetDirectories(comp))
                    {
                        var n = Path.GetFileName(d);
                        bool safe = n.Equals("QBUpdateCache", StringComparison.OrdinalIgnoreCase) ||
                            n.StartsWith("DownloadQB", StringComparison.OrdinalIgnoreCase) ||
                            n.Equals("SPatch", StringComparison.OrdinalIgnoreCase) ||
                            n.Equals("EPatch", StringComparison.OrdinalIgnoreCase);
                        log.Verbose($"{(safe ? "ALLOW" : "SKIP-not-allowlisted")} component {d}");
                        if (safe) SafeDelete.CleanDirAged(d, minAge, o.DryRun, s, log, qbRoot: dataRoot);
                    }
                }
                else log.Verbose($"MISS Components {comp}");
                if (Directory.Exists(dataRoot))
                {
                    foreach (var f in SafeEnumerate(dataRoot, "*.ADR.old", log))
                        SafeDelete.DeleteFileAged(f, minAge, o.DryRun, s, log, qbRoot: dataRoot);
                    foreach (var pat in new[] { "Qbwatch.log", "qbupdate.log", "*.log.tmp" })
                        foreach (var f in SafeEnumerate(dataRoot, pat, log))
                            SafeDelete.DeleteFileAged(f, minAge, o.DryRun, s, log, qbRoot: dataRoot);
                }
            }
        }

        // QBDataServiceUser* orphaned search_data temp
        var usersRoots = new List<string>();
        if (!string.IsNullOrEmpty(o.FixtureRoot)) usersRoots.Add(Path.Combine(o.FixtureRoot, "users"));
        else usersRoots.Add(Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Users"));
        foreach (var usersRoot in usersRoots.Where(Directory.Exists))
            foreach (var u in Directory.GetDirectories(usersRoot))
            {
                if (!Path.GetFileName(u).StartsWith("QBDataServiceUser", StringComparison.OrdinalIgnoreCase)) continue;
                var t = Path.Combine(u, "AppData", "Local", "Temp");
                log.Verbose($"QBSVCUSER dir={t} exists={Directory.Exists(t)}");
                if (!Directory.Exists(t)) continue;
                foreach (var f in Directory.GetFiles(t, "search_data.*.dat"))
                    SafeDelete.DeleteFileAged(f, minAge, o.DryRun, s, log);
                log.Info($"swept {t} (search_data orphan check)");
            }

        log.Verbose($"QuickBooksCleaner exit deleted={s.FilesDeleted} freed={s.BytesFreed} protected={s.FilesSkippedProtected} age={s.FilesSkippedAge}");
        return s;
    }

    static IEnumerable<string> SafeEnumerate(string root, string pattern, Logger log)
    {
        try { return Directory.GetFiles(root, pattern, SearchOption.AllDirectories); }
        catch (Exception ex) { log.Verbose($"LIST-FAIL {root} {pattern}: {ex.Message}"); return []; }
    }
}
