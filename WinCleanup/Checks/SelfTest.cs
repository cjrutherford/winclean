using WinCleanup.Cleaners;
using WinCleanup.Models;
using WinCleanup.Utils;

namespace WinCleanup.Checks;

// Builds a fake Windows+QuickBooks tree on ANY OS so the cleaner logic can be
// verified manually without a Windows machine:
//   <root>/win/Temp, WindowsTemp, SoftwareDistributionDownload
//   <root>/qb/QuickBooks Enterprise 24.0/{Program,Data}/...
//   <root>/users/QBDataServiceUser27/AppData/Local/Temp
// Old files (30d) = deletable; fresh files (1h) = must survive via age gate;
// *.qbw/.nd/.tlg/.ecml/Data1.cab = must ALWAYS survive via protection gate.
public static class SelfTest
{
    public static string BuildFixture(string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "wincleanup-fixture-" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var old_ = DateTime.Now.AddDays(-30);
        var fresh = DateTime.Now.AddHours(-1);

        void Mk(string rel, int bytes, DateTime mtime)
        {
            var p = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, new byte[bytes]);
            File.SetLastWriteTime(p, mtime);
        }

        // Windows temp: old junk (delete) + fresh (keep)
        Mk(Path.Combine("win", "Temp", "old.tmp"), 1024, old_);
        Mk(Path.Combine("win", "Temp", "fresh.tmp"), 1024, fresh);
        Mk(Path.Combine("win", "WindowsTemp", "old2.tmp"), 2048, old_);
        Mk(Path.Combine("win", "SoftwareDistributionDownload", "update-old.cab"), 4096, old_);

        // QB safe-to-delete
        var ver = "QuickBooks Enterprise 24.0";
        Mk(Path.Combine("qb", ver, "Data", "Components", "QBUpdateCache", "patch-old.msp"), 5120, old_);
        Mk(Path.Combine("qb", ver, "Data", "Components", "DownloadQB30", "dl-old.bin"), 5120, old_);
        Mk(Path.Combine("qb", ver, "Data", "Components", "SPatch", "sp-old.bin"), 1024, old_);
        Mk(Path.Combine("qb", ver, "Data", "company.ADR.old"), 1024, old_);
        Mk(Path.Combine("qb", ver, "Data", "Qbwatch.log"), 512, old_);
        // QB NEVER-delete (even though old)
        Mk(Path.Combine("qb", ver, "Data", "company.qbw"), 8192, old_);
        Mk(Path.Combine("qb", ver, "Data", "company.qbw.tlg"), 1024, old_);
        Mk(Path.Combine("qb", ver, "Data", "company.nd"), 256, old_);
        Mk(Path.Combine("qb", ver, "Data", "company.qbb"), 4096, old_);
        Mk(Path.Combine("qb", ver, "Program", "Components", "PConfig", "Data1.cab"), 4096, old_);
        Mk(Path.Combine("qb", ver, "Data", "EntitlementDataStore.ecml"), 256, old_);
        // QB service-user orphan temp
        Mk(Path.Combine("users", "QBDataServiceUser27", "AppData", "Local", "Temp", "search_data.12345.dat"), 3072, old_);

        // Fake Add/Remove inventory for Linux manual verification.
        File.WriteAllText(Path.Combine(root, "installed-apps.csv"),
            "Name,Version,Publisher\n" +
            "QuickBooks Enterprise 24.0,24.0.1,Intuit\n" +
            "Microsoft .NET Runtime 8.0,8.0.14,Microsoft\n" +
            "Acme Updater Helper,3.2,Acme\n" +
            "Old Trial Suite 2019,19.0,OldCo\n");
        return root;
    }

    public static int Run(CleanupOptions o, Logger log)
    {
        var root = string.IsNullOrEmpty(o.FixtureRoot) ? BuildFixture() : o.FixtureRoot;
        if (string.IsNullOrEmpty(o.FixtureRoot)) { o.FixtureRoot = root; o.LogDir = Path.Combine(root, "logs"); }
        log.Info($"self-test fixture: {root}");
        var installs = QuickBooksCleaner.Detect(log, o.FixtureRoot);
        log.Info($"self-test detected {installs.Count} QB installs (expect 1)");

        // Phase 1: dry-run — nothing may be deleted.
        o.DryRun = true;
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
        var wDry = Cleaners.WindowsCleaner.Run(o, log);
        var qDry = QuickBooksCleaner.Run(o, log, installs);
        var afterDry = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
        log.Info($"self-test dry-run: files before={before} after={afterDry} would-delete={wDry.FilesDeleted + qDry.FilesDeleted}");
        if (before != afterDry) { log.Error("SELF-TEST FAIL: dry-run deleted files"); return 10; }

        // Phase 2: real clean.
        o.DryRun = false;
        var w = Cleaners.WindowsCleaner.Run(o, log);
        var q = QuickBooksCleaner.Run(o, log, installs);

        bool Pass(string rel, bool mustExist)
        {
            bool exists = File.Exists(Path.Combine(root, rel));
            bool ok = exists == mustExist;
            log.Info($"self-test {(ok ? "PASS" : "FAIL")}: {(mustExist ? "kept " : "deleted ")}{rel} (exists={exists})");
            return ok;
        }
        var ver = "QuickBooks Enterprise 24.0";
        bool okAll =
            Pass(Path.Combine("win", "Temp", "old.tmp"), false) &
            Pass(Path.Combine("win", "Temp", "fresh.tmp"), true) &
            Pass(Path.Combine("qb", ver, "Data", "Components", "QBUpdateCache", "patch-old.msp"), false) &
            Pass(Path.Combine("users", "QBDataServiceUser27", "AppData", "Local", "Temp", "search_data.12345.dat"), false) &
            Pass(Path.Combine("qb", ver, "Data", "company.ADR.old"), false) &
            Pass(Path.Combine("qb", ver, "Data", "company.qbw"), true) &
            Pass(Path.Combine("qb", ver, "Data", "company.qbw.tlg"), true) &
            Pass(Path.Combine("qb", ver, "Data", "company.nd"), true) &
            Pass(Path.Combine("qb", ver, "Data", "company.qbb"), true) &
            Pass(Path.Combine("qb", ver, "Program", "Components", "PConfig", "Data1.cab"), true) &
            Pass(Path.Combine("qb", ver, "Data", "EntitlementDataStore.ecml"), true);

        log.Info($"self-test real: winDeleted={w.FilesDeleted} qbDeleted={q.FilesDeleted} result={(okAll ? "PASS" : "FAIL")}");

        // Phase 3: inventory + bottleneck against fixture CSV (Linux-verifiable).
        SoftwareInventory.Report(log, o.FixtureRoot);
        BottleneckReport.Run(log, o.FixtureRoot);
        var apps = SoftwareInventory.GetInstalledApps(log, o.FixtureRoot);
        bool invOk = apps.Any(a => a.Name.Contains("QuickBooks"));
        log.Info($"self-test inventory: apps={apps.Count} qbFound={invOk} result={(invOk ? "PASS" : "FAIL")}");
        return okAll && invOk ? 0 : 11;
    }
}
