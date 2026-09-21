using WinCleanup.Checks;
using WinCleanup.Cleaners;
using WinCleanup.Models;
using WinCleanup.Utils;

var o = Parse(args);
if (string.IsNullOrEmpty(o.FixtureRoot) && !OperatingSystem.IsWindows())
    o.LogDir = Path.Combine(Path.GetTempPath(), "WinCleanup", "logs");
using var log = new Logger(o.LogDir, verbose: o.Verbose);
log.Info($"WinCleanup v0.1.0 dryRun={o.DryRun} clean={o.DoClean} audit={o.DoAudit} minAge={o.MinAgeDays}d verbose={o.Verbose} fixture='{o.FixtureRoot}' selfTest={o.SelfTest}");
log.Verbose($"options: assumeYes={o.AssumeYes} allowQbRunning={o.AllowQbRunning} includePrefetch={o.IncludePrefetch} logDir={o.LogDir} os={Environment.OSVersion}");

if (o.SelfTest)
    return SelfTest.Run(o, log);

if (!OperatingSystem.IsWindows() && string.IsNullOrEmpty(o.FixtureRoot))
    log.Warn("not on Windows and no --fixture-root: audit/cleanup are no-ops. Use --self-test or --fixture-root=<dir> for manual verification on Linux.");

if (o.DoClean && !AdminHelper.IsAdmin() && OperatingSystem.IsWindows())
{
    log.Error("cleanup requires Administrator. Re-run elevated.");
    return 2;
}

var installs = QuickBooksCleaner.Detect(log, o.FixtureRoot);

if (o.DoAudit)
{
    if (!string.IsNullOrEmpty(o.FixtureRoot) || !OperatingSystem.IsWindows())
    {
        // Fixture/Linux manual verification: inventory + bottleneck run against fixture CSV.
        SoftwareInventory.Report(log, o.FixtureRoot);
        BottleneckReport.Run(log, o.FixtureRoot);
    }
    else
    {
        SystemAudit.Run(log);
        QbConfigCheck.Run(log, installs);
        SoftwareInventory.Report(log);
        BottleneckReport.Run(log);
    }
}

if (o.DoClean)
{
    if (AdminHelper.IsQuickBooksRunning() && !o.AllowQbRunning && string.IsNullOrEmpty(o.FixtureRoot))
    {
        log.Error("QuickBooks (QBW32/QBDBMgr) is running. Close it or pass --allow-qb-running.");
        return 3;
    }
    if (!o.AssumeYes && !o.DryRun && string.IsNullOrEmpty(o.FixtureRoot))
    {
        Console.Write("Type CLEAN to delete files: ");
        if (Console.ReadLine()?.Trim() != "CLEAN") { log.Info("aborted by user"); return 1; }
    }
    var w = WindowsCleaner.Run(o, log);
    var q = QuickBooksCleaner.Run(o, log, installs);
    log.Info($"windows: {w.FilesDeleted} files, {w.BytesFreed / 1e6:F1}MB freed, skipped locked={w.FilesSkippedLocked} age={w.FilesSkippedAge}");
    log.Info($"quickbooks: {q.FilesDeleted} files, {q.BytesFreed / 1e6:F1}MB freed, skipped protected={q.FilesSkippedProtected}");
    foreach (var e in w.Errors.Concat(q.Errors).Take(20)) log.Warn("err: " + e);
}

log.Info($"done. full verbose log: {log.Path}");
return 0;

static CleanupOptions Parse(string[] args)
{
    var o = new CleanupOptions();
    foreach (var a in args)
    {
        if (a == "--clean") { o.DoClean = true; o.DryRun = false; }
        else if (a == "--audit-only") { o.DoClean = false; o.DoAudit = true; o.DryRun = true; }
        else if (a == "--dry-run") o.DryRun = true;
        else if (a is "--yes" or "-y") o.AssumeYes = true;
        else if (a == "--allow-qb-running") o.AllowQbRunning = true;
        else if (a == "--include-prefetch") o.IncludePrefetch = true;
        else if (a == "--verbose") o.Verbose = true;
        else if (a == "--quiet") o.Verbose = false;
        else if (a == "--self-test") { o.SelfTest = true; o.Verbose = true; }
        else if (a.StartsWith("--fixture-root=")) o.FixtureRoot = a["--fixture-root=".Length..];
        else if (a.StartsWith("--min-age=") && int.TryParse(a["--min-age=".Length..], out var d)) o.MinAgeDays = d;
        else if (a.StartsWith("--log-dir=")) o.LogDir = a["--log-dir=".Length..];
        else if (a is "--help" or "-h")
        {
            Console.WriteLine("""
                WinCleanup — Windows + QuickBooks-safe cleanup & audit (verbose by default)
                Usage:
                  WinCleanup [--audit-only] [--clean] [--dry-run] [--yes]
                             [--min-age=7] [--allow-qb-running] [--include-prefetch]
                             [--verbose|--quiet] [--log-dir=DIR]
                  WinCleanup --self-test [--min-age=7]
                  WinCleanup --clean --fixture-root=/tmp/myfixture --min-age=7

                Audit = system (disk/RAM/CPU/SMART/eventlog/startup/updates/net)
                      + QuickBooks config + software inventory + bottleneck report.
                Manual verification without Windows (Linux):
                  dotnet run -- --self-test
                  dotnet run -- --audit-only --fixture-root=/tmp/myfixture --verbose
                  dotnet run -- --clean --fixture-root=/tmp/myfixture --dry-run --verbose
                On Windows:
                  WinCleanup.exe --audit-only --verbose
                  WinCleanup.exe --clean --dry-run --verbose   (safe preview)
                  WinCleanup.exe --clean --yes                 (needs Admin, QB closed)
                Log: every file decision (DELETE/SKIP reason/ENTER/EXIT) at VERBOSE level.
                """);
            Environment.Exit(0);
        }
    }
    return o;
}
