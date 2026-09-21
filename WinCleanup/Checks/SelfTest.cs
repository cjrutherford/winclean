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
        // Includes a second AV (dual-engine conflict) + OEM bloat for 0.2 scan.
        File.WriteAllText(Path.Combine(root, "installed-apps.csv"),
            "Name,Version,Publisher\n" +
            "QuickBooks Enterprise 24.0,24.0.1,Intuit\n" +
            "Microsoft .NET Runtime 8.0,8.0.14,Microsoft\n" +
            "Acme Updater Helper,3.2,Acme\n" +
            "Old Trial Suite 2019,19.0,OldCo\n" +
            "Contoso Antivirus Plus,9.1,Contoso\n" +
            "Norton 360,22.0,Norton\n" +
            "McAfee LiveSafe,16.0,McAfee\n" +
            "Dell SupportAssist,4.0,Dell\n");
        // Fake Appx packages + scheduled tasks for 0.2 offender scan.
        File.WriteAllText(Path.Combine(root, "appx-packages.csv"),
            "Name\n" +
            "Microsoft.XboxApp\n" +
            "Microsoft.BingNews\n" +
            "Microsoft.WindowsCalculator\n");
        File.WriteAllText(Path.Combine(root, "tasks.csv"),
            "Name,State\n" +
            "Adobe Acrobat Update Task,Ready\n" +
            "Adobe Genuine Software Monitor,Ready\n" +
            "GoogleUpdateTaskMachineCore,Running\n");
        // Fake startup + services for startup-impact and lean-profile checks.
        File.WriteAllText(Path.Combine(root, "startup.csv"),
            "Scope,Name,Command\n" +
            "CurrentUser/Registry64,Spotify,C:\\Users\\test\\AppData\\Roaming\\Spotify\\Spotify.exe\n" +
            "LocalMachine/Registry32,AdobeARM,C:\\Program Files\\Adobe\\ARM\\AdobeARM.exe\n" +
            "CurrentUser/Registry64,SomeHelper,C:\\Tools\\SomeHelper.exe\n" +
            "LocalMachine/Registry64,SecurityHealth,C:\\Windows\\System32\\SecurityHealthSystray.exe\n");
        File.WriteAllText(Path.Combine(root, "services.csv"),
            "Name,StartType,State\n" +
            "XblGameSave,2 AUTO_START,RUNNING\n" +
            "SysMain,2 AUTO_START,RUNNING\n");
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

        // Phase 4: 0.2 offender scan — dual-AV flagged, Tier-0 dry-run changes nothing.
        var offenders = OffenderScan.Scan(log, o, installs);
        offenders.AddRange(StartupAnalyzer.Analyze(log, o));
        o.Lean = true;
        offenders.AddRange(LeanProfile.Analyze(log, o, installs));
        bool dualAv = offenders.Any(f => f.Category == "SecurityConflicts" && (f.Name.Contains("McAfee") || f.Name.Contains("Norton")));
        bool appx = offenders.Any(f => f.Category == "InboxAppx");
        bool updater = offenders.Any(f => f.Category == "UpdaterSprawl");
        bool oem = offenders.Any(f => f.Category == "OemBloat");
        bool startup = offenders.Any(f => f.Category == "StartupImpact" && f.Tier == "Tier0");
        bool leanSvc = offenders.Any(f => f.Category == "LeanService" && f.Name == "service::XblGameSave");
        log.Info($"self-test offenders: total={offenders.Count} dualAv={dualAv} appx={appx} updater={updater} oem={oem} startup={startup} leanSvc={leanSvc}");
        o.DryRun = true;
        int filesBeforeMitigate = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
        int wouldApply = Mitigate.ApplyTier0(log, o, offenders, installs);
        int filesAfterMitigate = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
        bool mitigateDryOk = filesBeforeMitigate == filesAfterMitigate && wouldApply > 0;
        log.Info($"self-test mitigate dry-run: wouldApply={wouldApply} filesUnchanged={filesBeforeMitigate == filesAfterMitigate} result={(mitigateDryOk ? "PASS" : "FAIL")}");
        bool phase4 = dualAv && appx && updater && oem && startup && leanSvc && mitigateDryOk;
        log.Info($"self-test phase4 result={(phase4 ? "PASS" : "FAIL")}");
        return okAll && invOk && phase4 ? 0 : 11;
    }
}
