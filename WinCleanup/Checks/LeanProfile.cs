using WinCleanup.Cleaners;
using WinCleanup.Models;
using WinCleanup.Utils;

namespace WinCleanup.Checks;

// Constrained-environment profile (--lean): services + optional features safe
// to turn off, plus power/visual guidance. Tier-0 covers ONLY the tiny safe
// list (gaming + maps services) with start-type backup; everything else is
// Tier-1 guided commands. QuickBooks hosting and security/networking services
// are never touched; a QB host gets sharing-service exemptions noted.
public static class LeanProfile
{
    // Tier-0: universally safe to set Manual + stop (gaming, maps). Backed up.
    public static readonly string[] Tier0Services =
        ["XblGameSave", "XboxGipSvc", "XboxNetApiSvc", "MapsBroker"];

    // Guided: recommend only when the condition holds; print exact commands.
    sealed record GuidedService(string Name, string When, string Why);
    static readonly GuidedService[] Guided =
    [
        new("SysMain", "HDD (not SSD) only — check Defrag media type", "SysMain preload thrashes spinning disks; useless on SSD"),
        new("Spooler", "no printer installed — Get-Printer returns nothing", "print spooler idles on print-less boxes"),
        new("BTAGService", "no Bluetooth radio — Get-PnpDevice finds none", "bluetooth stack without hardware"),
        new("DiagTrack", "always optional", "telemetry; safe to disable (Manual)"),
        new("WSearch", "no Outlook desktop search reliance", "indexer I/O; disable only if Outlook search is expendable"),
        new("Fax", "no fax modem", "fax service on modem-less boxes"),
    ];

    static readonly string[] NeverTouch =
    [
        "QBDBMgr", "QBDBMgrN", "QBCFMonitorService", "QBUpdateService", "QuickBooks",
        "WinDefend", "MpsSvc", "wuauserv", "BITS", "LanmanServer", "LanmanWorkstation",
        "Dnscache", "Dhcp", "EventLog", "RpcSs",
    ];

    // Optional Windows features review list (guided DISM only, never automatic).
    static readonly (string Feature, string Why)[] OptionalFeatures =
    [
        ("SMB1Protocol", "insecure legacy protocol — disable unless ancient NAS needs it"),
        ("Internet-Explorer-Optional-amd64", "retired browser engine"),
        ("WorkFolders-Client", "enterprise sync most boxes never use"),
        ("TelnetClient", "insecure; use SSH"),
        ("TFTP", "rarely needed outside PXE admins"),
    ];

    public static List<Offender> Analyze(Logger log, CleanupOptions o, List<QuickBooksCleaner.Install> installs)
    {
        var found = new List<Offender>();
        bool qbHost = installs.Count > 0;
        if (qbHost)
            log.Info("lean: QuickBooks present — file-sharing services (LanmanServer/Workstation) exempt from all suggestions");

        var names = Tier0Services.Concat(Guided.Select(g => g.Name)).Distinct(StringComparer.OrdinalIgnoreCase);
        var svcs = SoftwareInventory.GetServices(log, names, o.FixtureRoot);
        var byName = svcs.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var svc in Tier0Services)
        {
            if (!byName.TryGetValue(svc, out var e)) { log.Verbose($"lean: {svc} not installed — skip"); continue; }
            if (!e.StartType.Contains("AUTO", StringComparison.OrdinalIgnoreCase))
            { log.Verbose($"lean: {svc} already {e.StartType} — skip"); continue; }
            string evidence = $"Service {svc} is AUTO_START ({e.State}); gaming/maps service, safe to set Manual.";
            log.Verbose($"lean: {evidence}");
            found.Add(new Offender("LeanService", $"service::{svc}", 35, 1.0, evidence, "Tier0",
                $"sc config {svc} start= demand && sc stop {svc}  (or --mitigate)"));
        }

        foreach (var g in Guided)
        {
            if (!byName.TryGetValue(g.Name, out var e)) { log.Verbose($"lean: {g.Name} not installed — skip"); continue; }
            if (!e.StartType.Contains("AUTO", StringComparison.OrdinalIgnoreCase)) continue;
            string evidence = $"Service {g.Name} is AUTO_START ({e.State}); {g.Why}. Condition: {g.When}.";
            log.Verbose($"lean: {evidence}");
            found.Add(new Offender("LeanService", g.Name, 30, 1.0, evidence, "Tier1",
                $"Verify condition, then: sc config {g.Name} start= demand && sc stop {g.Name}"));
        }
        return found;
    }

    public static void Report(Logger log, CleanupOptions o, List<QuickBooksCleaner.Install> installs)
    {
        log.Info("=== LEAN PROFILE (constrained environments) ===");
        var offenders = Analyze(log, o, installs);
        log.Info($"lean services flagged: {offenders.Count} (Tier-0 safe list: {Tier0Services.Length})");
        log.Info("Optional Windows features to review (guided only, never automatic):");
        foreach (var (f, why) in OptionalFeatures)
            log.Info($"  DISM /Online /Disable-Feature /FeatureName:{f} /NoRestart   # {why}");
        log.Info("Power/visual quick wins: powercfg /setactive SCHEME_MIN (or Balanced on laptops); "
            + "System > Advanced > Performance > Adjust for best performance (keep thumbnails if wanted); "
            + "enable Storage Sense.");
        log.Info($"Protected, never suggested: {string.Join(", ", NeverTouch.Take(8))}… (full list in verbose log)");
        log.Verbose($"lean protected list: {string.Join(", ", NeverTouch)}");
    }
}
