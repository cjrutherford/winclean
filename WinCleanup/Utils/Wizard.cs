using WinCleanup.Models;

namespace WinCleanup.Utils;

// Menu-driven run builder: the primary interface when WinCleanup starts with
// no flags on an interactive console. Flags remain as the scriptable fallback.
// The wizard shows the equivalent command line before running, so every menu
// run is reproducible as flags.
public static class Wizard
{
    // Returns configured options, or null when the user cancels.
    public static CleanupOptions? Run(Logger log, Display display, CleanupOptions o)
    {
        display.Section("WinCleanup setup");
        Console.WriteLine("  Build your run below. Flags remain available for scripts (--help).");
        Console.WriteLine();

        var tasks = Menu.PickMany("What should WinCleanup do?",
            ["System audit (disk, RAM, services, QB config, inventory)",
             "Worst-offender scan (AV, bloat, updaters, startup, QB blockers)",
             "Clean temp & cache files",
             "Apply Tier-0 fixes (startup, updater tasks, safe services, AV exclusions)",
             "Lean profile (constrained-box service & feature guidance)"],
            ["Read-only system health + configuration check",
             "Ranked fix-list with evidence; nothing is changed",
             "Age-gated temp/cache cleanup; skips locked + protected files",
             "Consent-gated; every action backed up to an undo bundle",
             "Extra service/feature guidance for slow or small machines"]);
        if (tasks.Count == 0) { log.Info("wizard: nothing selected — cancelled"); return null; }

        bool audit = tasks.Contains(0);
        bool scan = tasks.Contains(1);
        bool clean = tasks.Contains(2);
        bool mitigate = tasks.Contains(3);
        bool lean = tasks.Contains(4);
        if (mitigate && !scan)
        {
            scan = true;
            log.Info("wizard: offender scan auto-enabled (Tier-0 needs scan results)");
        }

        int mode = Menu.PickOne("How should it run?",
            ["Dry-run preview (safe — nothing is changed)",
             "Live run (makes changes — needs Admin for clean/mitigate)"]);
        if (mode < 0) { log.Info("wizard: cancelled at mode step"); return null; }
        bool live = mode == 1;

        if (live && (clean || mitigate))
        {
            int confirm = Menu.PickOne("Live run confirmation",
                ["Yes — run live now", "Switch back to dry-run preview"]);
            if (confirm != 0) live = false;
        }

        o.DoAudit = audit;
        o.DoClean = clean;
        o.DryRun = !live;
        o.AssumeYes = live; // menu consent replaces typed confirmation
        o.OffendersOnly = scan && !audit && !clean;
        o.Mitigate = mitigate;
        o.Lean = lean;
        o.Verbose = true;

        string cmd = ToCommandLine(o);
        Console.WriteLine();
        display.Line($"Equivalent command: {cmd}");
        display.Line("  Save this line to repeat the exact run from scripts.");
        log.Info($"wizard: equivalent command: {cmd}");
        int go = Menu.PickOne("Ready?", ["Run now", "Cancel"]);
        if (go != 0) { log.Info("wizard: cancelled at confirm step"); return null; }
        return o;
    }

    public static string ToCommandLine(CleanupOptions o)
    {
        var parts = new List<string> { "WinCleanup.exe" };
        if (o.OffendersOnly) parts.Add("--offenders");
        else if (o.DoAudit) parts.Add("--audit-only");
        if (o.DoClean) parts.Add("--clean");
        if (o.Mitigate) parts.Add("--mitigate");
        if (o.Lean) parts.Add("--lean");
        if (o.DryRun) parts.Add("--dry-run"); else parts.Add("--yes");
        if (o.IncludePrefetch) parts.Add("--include-prefetch");
        if (o.AllowQbRunning) parts.Add("--allow-qb-running");
        if (o.MinAgeDays != 7) parts.Add($"--min-age={o.MinAgeDays}");
        return string.Join(" ", parts);
    }
}
