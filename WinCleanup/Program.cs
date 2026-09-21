using WinCleanup.Checks;
using WinCleanup.Cleaners;
using WinCleanup.Models;
using WinCleanup.Utils;

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try { Console.WriteLine($"FATAL: {e.ExceptionObject}"); } catch { }
};

var o = Parse(args);
bool noFlags = args.Length == 0;
string defaultLogDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "WinCleanup", "logs");
if (!string.IsNullOrEmpty(o.FixtureRoot) && o.LogDir == defaultLogDir)
    o.LogDir = Path.Combine(o.FixtureRoot, "logs"); // fixture runs stay self-contained
if (string.IsNullOrEmpty(o.FixtureRoot) && !OperatingSystem.IsWindows())
    o.LogDir = Path.Combine(Path.GetTempPath(), "WinCleanup", "logs");
using var log = new Logger(o.LogDir, verbose: o.Verbose);
Logger.Prune(o.LogDir, keep: 10); // log rolling: keep the 10 newest logs
if (o.Color == "always") Tui.ColorEnabled = true;
else if (o.Color == "never") Tui.ColorEnabled = false;
var display = new Display(log);

// Primary interface: menu-driven wizard when started bare on a console.
// Flags remain as the scriptable fallback (and whenever input is piped).
if ((noFlags || o.Menu) && !o.SelfTest && Menu.IsInteractive)
{
    var picked = Wizard.Run(log, display, o);
    if (picked is null) { log.Info("cancelled from setup menu."); return 0; }
    o = picked;
    log.VerboseEnabled = o.Verbose;
}
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

var stages = new List<string> { "Detect QuickBooks" };
if (o.OffendersOnly) stages.Add("Offender scan");
else
{
    if (o.DoAudit) stages.Add("Audit");
    if (o.DoAudit || o.Mitigate) stages.Add("Offender scan");
    if (o.DoClean) stages.Add("Cleanup");
}
if (o.Lean) stages.Add("Lean profile");
stages.Add("Summary");
var plan = new StagePlan(log, stages);

plan.Begin("Detect QuickBooks");
var installs = QuickBooksCleaner.Detect(log, o.FixtureRoot);
var summary = new Summary();
summary.Mode($"{(o.DoClean ? "clean" : "audit")}{(o.OffendersOnly ? " + offenders" : "")}{(o.Mitigate ? " + mitigate" : "")}{(o.DryRun ? " (dry-run)" : " (LIVE)")}");

if (o.OffendersOnly)
{
    plan.Begin("Offender scan");
    var (offenders, mitigated) = RunOffenders(log, o, installs, plan, display);
    summary.TopOffenders(offenders);
    summary.Action(offenders.Count == 0 ? "No offenders found" : $"{offenders.Count} offenders ranked");
    if (mitigated > 0) summary.Action($"Tier-0 mitigation: {mitigated} actions{(o.DryRun ? " (dry-run preview)" : "")}; undo bundle in {o.LogDir}");
    if (!o.Mitigate && offenders.Any(f => f.Tier == "Tier0")) summary.Next("Preview Tier-0 fixes: --offenders --mitigate --dry-run");
    if (o.Lean)
    {
        plan.Begin("Lean profile");
        LeanProfile.Report(log, o, installs);
        summary.Finding("Lean profile reviewed — see LEAN PROFILE section");
    }
    summary.Print(log, log.Path);
    log.Info($"done. full verbose log: {log.Path}");
    return 0;
}

if (o.DoAudit)
{
    plan.Begin("Audit");
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

if (!o.OffendersOnly && (o.DoAudit || o.Mitigate))
{
    plan.Begin("Offender scan");
    var (offenders, mitigated) = RunOffenders(log, o, installs, plan, display);
    summary.TopOffenders(offenders);
    summary.Action($"{offenders.Count} offenders ranked");
    if (mitigated > 0) summary.Action($"Tier-0 mitigation: {mitigated} actions{(o.DryRun ? " (dry-run preview)" : "")}; undo bundle in {o.LogDir}");
    if (!o.Mitigate && offenders.Any(f => f.Tier == "Tier0")) summary.Next("Preview Tier-0 fixes: --offenders --mitigate --dry-run");
}

if (o.DoClean)
{
    plan.Begin("Cleanup");
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
    summary.Action(o.DryRun
        ? $"Would delete {w.FilesDeleted + q.FilesDeleted} files (~{(w.BytesFreed + q.BytesFreed) / 1e6:F1}MB) — dry-run, nothing removed"
        : $"Deleted {w.FilesDeleted + q.FilesDeleted} files (~{(w.BytesFreed + q.BytesFreed) / 1e6:F1}MB freed)");
    if (o.DryRun) summary.Next("Apply for real: --clean --yes (Admin, QB closed)");
}

if (o.Lean && !o.OffendersOnly)
{
    plan.Begin("Lean profile");
    LeanProfile.Report(log, o, installs);
    summary.Finding("Lean profile reviewed — see LEAN PROFILE section");
}

plan.Begin("Summary");
summary.Print(log, log.Path);
log.Info($"done. full verbose log: {log.Path}");
return 0;

static (List<Offender> offenders, int mitigated) RunOffenders(Logger log, CleanupOptions o, List<QuickBooksCleaner.Install> installs, StagePlan plan, Display display)
{
    display.Section("Worst-offender scan");
    var offenders = OffenderScan.Scan(log, o, installs);
    offenders.AddRange(QbFirewall.Check(log, installs));
    offenders.AddRange(StartupAnalyzer.Analyze(log, o));
    if (o.Lean) offenders.AddRange(LeanProfile.Analyze(log, o, installs));
    offenders = offenders.OrderByDescending(x => x.Score * x.ConflictMultiplier).ToList();
    log.Info($"offenders ranked: {offenders.Count}");
    int rank = 0;
    foreach (var f in offenders.Take(25))
        display.OffenderCard(++rank, f);
    if (installs.Count > 0) QbFirewall.PrintRules(log, installs);
    int mitigated = 0;
    if (o.Mitigate)
    {
        var tier0 = offenders.Where(f => f.Tier == "Tier0").ToList();
        List<Offender> chosen = tier0;
        if (!o.AssumeYes && !o.DryRun && string.IsNullOrEmpty(o.FixtureRoot))
        {
            if (tier0.Count == 0) log.Info("No Tier-0 fixes to apply.");
            else if (Menu.IsInteractive)
            {
                var picked = Menu.PickMany("Tier-0 fixes — space to toggle, enter to apply",
                    tier0.Select(t => t.Name).ToList(),
                    tier0.Select(t => t.Evidence).ToList());
                chosen = picked.Select(i => tier0[i]).ToList();
                log.Info($"Tier-0 selection: {chosen.Count}/{tier0.Count} picked (consented via menu)");
            }
            else
            {
                Console.Write("Type MITIGATE to apply all Tier-0 fixes: ");
                if (Console.ReadLine()?.Trim() != "MITIGATE") { log.Info("mitigation aborted by user"); return (offenders, 0); }
            }
        }
        mitigated = Mitigate.ApplyTier0(log, o, chosen, installs);
        log.Info($"Tier-0 mitigation: {mitigated} actions{(o.DryRun ? " (dry-run)" : "")}");
    }
    else if (offenders.Any(f => f.Tier == "Tier0"))
        log.Info("Tier-0 fixes available — re-run with --mitigate [--dry-run] to preview/apply");
    return (offenders, mitigated);
}

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
        else if (a == "--color") o.Color = "always";
        else if (a == "--no-color") o.Color = "never";
        else if (a == "--self-test") { o.SelfTest = true; o.Verbose = true; }
        else if (a == "--offenders") { o.OffendersOnly = true; o.DoClean = false; o.Verbose = true; }
        else if (a == "--mitigate") o.Mitigate = true;
        else if (a == "--lean") o.Lean = true;
        else if (a == "--menu") o.Menu = true;
        else if (a == "--no-audit") o.DoAudit = false;
        else if (a.StartsWith("--fixture-root=")) o.FixtureRoot = a["--fixture-root=".Length..];
        else if (a.StartsWith("--min-age=") && int.TryParse(a["--min-age=".Length..], out var d)) o.MinAgeDays = d;
        else if (a.StartsWith("--log-dir=")) o.LogDir = a["--log-dir=".Length..];
        else if (a is "--help" or "-h")
        {
            Console.WriteLine("""
                WinCleanup — Windows + QuickBooks-safe cleanup & audit (verbose by default)

                No flags on an interactive console opens the setup wizard, which
                shows the equivalent command line before running. Flags below are
                the scriptable fallback (and are used whenever input is piped).
                Usage:
                  WinCleanup [--audit-only] [--clean] [--dry-run] [--yes]
                             [--min-age=7] [--allow-qb-running] [--include-prefetch]
                             [--verbose|--quiet] [--log-dir=DIR]
                  WinCleanup --self-test [--min-age=7]
                  WinCleanup --offenders [--mitigate] [--dry-run|--yes]
                  WinCleanup --clean --fixture-root=/tmp/myfixture --min-age=7
                  [--color|--no-color] forces display color on/off (auto otherwise).

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
