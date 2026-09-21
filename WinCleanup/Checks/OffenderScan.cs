using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinCleanup.Cleaners;
using WinCleanup.Models;
using WinCleanup.Utils;

namespace WinCleanup.Checks;

// 0.2 offender detection: five read-only detectors over inventory + QB installs.
// Results sorted by Score*ConflictMultiplier descending, Score clamped 0-100.
// Every detection is Verbose-logged with evidence; each offender gets one Info line.
public static class OffenderScan
{
    public static List<Offender> Scan(Logger log, CleanupOptions o, List<QuickBooksCleaner.Install> installs)
    {
        installs ??= new List<QuickBooksCleaner.Install>();
        string fixtureRoot = o?.FixtureRoot ?? "";

        var found = new List<Offender>();
        DetectSecurityConflicts(log, fixtureRoot, installs, found);
        DetectInboxAppx(log, fixtureRoot, found);
        DetectUpdaterSprawl(log, fixtureRoot, found);
        DetectOemBloat(log, fixtureRoot, found);
        DetectQbBlockers(log, fixtureRoot, installs, found);

        var result = found
            .Select(x => x with { Score = Math.Clamp(x.Score, 0, 100) })
            .OrderByDescending(x => x.Score * x.ConflictMultiplier)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Category, StringComparer.Ordinal)
            .ToList();

        foreach (var off in result)
            log.Info($"offender [{off.Category}/{off.Tier}] score={off.Score}x{off.ConflictMultiplier} :: {off.Name} — {off.Evidence}");

        return result;
    }

    // ---- 1. SecurityConflicts ----
    static void DetectSecurityConflicts(Logger log, string fixtureRoot,
        List<QuickBooksCleaner.Install> installs, List<Offender> out_)
    {
        // Active engine resolution (Defender vs third-party) lives in SecurityEngine.
        string[] avKeys =
        [
            "Norton", "McAfee", "Trend Micro", "Avast", "AVG", "Kaspersky",
            "Bitdefender", "ESET", "Sophos", "Malwarebytes", "TotalAV", "Webroot",
        ];
        var apps = SoftwareInventory.GetInstalledApps(log, fixtureRoot);
        var thirdParty = apps
            .Where(a => avKeys.Any(k => a.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (var a in thirdParty)
            log.Verbose($"security: third-party AV match '{a.Name}' pub='{a.Publisher}'");

        var engines = new List<string>();
        var engineState = SecurityEngine.Resolve(log, fixtureRoot, thirdParty.Select(a => a.Name).ToList());
        log.Verbose($"security: active engine = {engineState.ActiveEngine}");
        if (engineState.DefenderRealTime) engines.Add("Windows Defender");
        engines.AddRange(thirdParty.Select(a => a.Name));

        if (engines.Count >= 2)
        {
            string all = string.Join("; ", engines);
            foreach (var extra in engines.Skip(1))
            {
                string evidence = $"Real-time AV engines detected ({engines.Count}): {all}. '{extra}' is surplus.";
                log.Verbose($"security: dual-AV {evidence}");
                out_.Add(new Offender(
                    "SecurityConflicts", extra, 80, 2.0, evidence, "Tier1",
                    "Keep ONE real-time AV; uninstall or passive-mode the other (manual, vendor uninstaller)"));
            }
        }
        else log.Verbose($"security: engines={engines.Count} ({string.Join("; ", engines)}) — no conflict");

        // Defender exclusion covering each QB install (PowerShell returns "" off-Windows).
        if (installs.Count > 0)
        {
            var exclRaw = SysProbe.PowerShell(
                "Get-MpPreference | Select-Object -ExpandProperty ExclusionPath", 30000, log);
            var exclusions = exclRaw
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Where(s => s.Length > 0)
                .ToList();
            log.Verbose($"security: defender exclusions={exclusions.Count} [{string.Join("; ", exclusions)}]");
            foreach (var inst in installs)
            {
                string qbdir = !string.IsNullOrWhiteSpace(inst.ProgramDir) ? inst.ProgramDir : inst.DataDir;
                if (string.IsNullOrWhiteSpace(qbdir)) { log.Verbose($"security: QB {inst.Version} has no dir — skip exclusion check"); continue; }
                string normQb = qbdir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                bool covered = exclusions.Any(e => normQb.StartsWith(e, StringComparison.OrdinalIgnoreCase));
                if (covered) log.Verbose($"security: QB {inst.Version} at {qbdir} covered by Defender exclusion");
                else if (engineState.DefenderRealTime)
                {
                    string evidence = $"QuickBooks {inst.Version} at {qbdir} has no Defender ExclusionPath covering it.";
                    log.Verbose($"security: missing-exclusion {evidence}");
                    out_.Add(new Offender(
                        "SecurityConflicts", $"Defender exclusion missing for {inst.Version}",
                        70, 1.5, evidence, "Tier0",
                        $"Add-MpPreference -ExclusionPath '{qbdir}'"));
                }
                else
                {
                    // Defender exclusions are inert while another engine owns real-time
                    // protection — route to Tier-1 vendor guidance instead of Tier-0.
                    string evidence = $"QuickBooks {inst.Version} at {qbdir} is scanned by {engineState.ActiveEngine}; Defender exclusions would be inert.";
                    log.Verbose($"security: vendor-exclusion-needed {evidence}");
                    out_.Add(new Offender(
                        "SecurityConflicts", $"AV exclusion missing for {inst.Version} (active: {engineState.ActiveEngine})",
                        70, 1.5, evidence, "Tier1",
                        $"Add an exclusion for '{qbdir}' in the {engineState.ActiveEngine} console (allowed programs / real-time exclusions), then re-run --offenders to verify"));
                }
            }
        }
        else log.Verbose("security: no QB installs — skip exclusion check");
    }

    // ---- 2. InboxAppx ----
    static void DetectInboxAppx(Logger log, string fixtureRoot, List<Offender> out_)
    {
        string[] blocklist =
        [
            "Xbox", "SolitaireCollection", "Clipchamp", "BingNews", "BingWeather",
            "Copilot", "Cortana", "MicrosoftTeams", "FeedbackHub", "BingMaps",
            "ZuneMusic", "ZuneVideo", "People", "SkypeApp", "MixedReality", "3DViewer",
        ];

        var packages = new List<string>();
        if (!string.IsNullOrEmpty(fixtureRoot))
        {
            var csv = Path.Combine(fixtureRoot, "appx-packages.csv");
            if (File.Exists(csv))
            {
                foreach (var line in File.ReadAllLines(csv).Skip(1))
                {
                    var n = line.Trim().Trim('"');
                    if (n.Length > 0) packages.Add(n);
                }
                log.Verbose($"appx: {packages.Count} packages from fixture {csv}");
            }
            else log.Verbose($"appx: no fixture csv at {csv}");
        }
        else if (OperatingSystem.IsWindows())
        {
            var ps = SysProbe.PowerShell("Get-AppxPackage | Select-Object Name", 30000, log);
            foreach (var line in ps.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var n = line.Trim();
                if (n.Length == 0) continue;
                if (n.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                if (n.All(c => c == '-' || c == ' ')) continue;
                packages.Add(n);
            }
            log.Verbose($"appx: {packages.Count} packages from Get-AppxPackage");
        }
        else log.Verbose("appx: skipped non-Windows, no fixture");

        foreach (var pkg in packages.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = blocklist.FirstOrDefault(k => pkg.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (key == null) continue;
            string evidence = $"Inbox Appx '{pkg}' matches blocklist '{key}'.";
            if (key.Equals("MicrosoftTeams", StringComparison.OrdinalIgnoreCase))
                evidence += " Personal only — keep work/school Teams.";
            log.Verbose($"appx: {evidence}");
            out_.Add(new Offender(
                "InboxAppx", pkg, 25, 1.0, evidence, "Tier1",
                $"Get-AppxPackage *{pkg}* | Remove-AppxPackage"));
        }
    }

    // ---- 3. UpdaterSprawl ----
    static void DetectUpdaterSprawl(Logger log, string fixtureRoot, List<Offender> out_)
    {
        string[] vendors =
        [
            "Adobe", "GoogleUpdate", "Apple Software Update", "Opera",
            "BraveUpdate", "Firefox Background Update", "OneDrive Standalone Update",
            "Office Automatic Updates",
        ];

        var tasks = new List<(string Name, string State)>();
        if (!string.IsNullOrEmpty(fixtureRoot))
        {
            var csv = Path.Combine(fixtureRoot, "tasks.csv");
            if (File.Exists(csv))
            {
                foreach (var line in File.ReadAllLines(csv).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int i = line.LastIndexOf(',');
                    if (i < 0) continue;
                    var name = line[..i].Trim().Trim('"');
                    var state = line[(i + 1)..].Trim().Trim('"');
                    if (name.Length > 0) tasks.Add((name, state));
                }
                log.Verbose($"updaters: {tasks.Count} tasks from fixture {csv}");
            }
            else log.Verbose($"updaters: no fixture csv at {csv}");
        }
        else
        {
            // Already filtered to Ready/Running on Windows, empty off-Windows.
            foreach (var t in SoftwareInventory.GetScheduledTasks(log))
                tasks.Add((t.Name, t.State));
            log.Verbose($"updaters: {tasks.Count} ready/running tasks from schtasks");
        }

        bool Ready(string s) =>
            s.Equals("Ready", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Running", StringComparison.OrdinalIgnoreCase);

        var matched = new List<(string Task, string Vendor)>();
        foreach (var (name, state) in tasks)
        {
            if (!Ready(state)) continue;
            var v = vendors.FirstOrDefault(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (v != null) matched.Add((name, v));
        }

        foreach (var g in matched.GroupBy(x => x.Vendor, StringComparer.OrdinalIgnoreCase))
        {
            var list = g.Select(x => x.Task).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count <= 1) { log.Verbose($"updaters: vendor '{g.Key}' single task — keep"); continue; }
            string kept = list[0];
            foreach (var surplus in list.Skip(1))
            {
                string evidence = $"Updater vendor '{g.Key}' has {list.Count} ready/running tasks; surplus '{surplus}' (keep '{kept}').";
                log.Verbose($"updaters: {evidence}");
                out_.Add(new Offender(
                    "UpdaterSprawl", surplus, 30, 1.0, evidence, "Tier0",
                    $"schtasks /change /tn \"{surplus}\" /disable"));
            }
        }
    }

    // ---- 4. OemBloat ----
    static void DetectOemBloat(Logger log, string fixtureRoot, List<Offender> out_)
    {
        string[] oem =
        [
            "Dell", "HP", "Hewlett", "Lenovo", "ASUS", "Acer", "MSI", "Samsung", "Toshiba",
        ];
        var apps = SoftwareInventory.GetInstalledApps(log, fixtureRoot);
        // Empty off-Windows; populated from Run keys + Startup folders on Windows.
        var startups = SoftwareInventory.GetStartupEntries(log);

        foreach (var app in apps)
        {
            var key = oem.FirstOrDefault(k =>
                (!string.IsNullOrEmpty(app.Publisher) &&
                 app.Publisher.Contains(k, StringComparison.OrdinalIgnoreCase)));
            if (key == null) continue;
            var se = startups.FirstOrDefault(s =>
                s.Name.Contains(app.Name, StringComparison.OrdinalIgnoreCase) ||
                s.Command.Contains(app.Name, StringComparison.OrdinalIgnoreCase) ||
                app.Name.Contains(s.Name, StringComparison.OrdinalIgnoreCase));
            int score = se == null ? 20 : 30;
            string evidence = $"OEM publisher '{app.Publisher}' for app '{app.Name}'.";
            if (se != null) evidence += $" Startup entry '{se.Name}' present (+10).";
            log.Verbose($"oem: {evidence}");
            out_.Add(new Offender(
                "OemBloat", app.Name, score, 1.0, evidence, "Tier1",
                "Review in Settings > Apps; keep drivers/firmware, remove marketing/support assistants"));
        }
    }

    // ---- 5. QbBlockers ----
    static void DetectQbBlockers(Logger log, string fixtureRoot,
        List<QuickBooksCleaner.Install> installs, List<Offender> out_)
    {
        var qbwFiles = new List<string>();
        if (!string.IsNullOrEmpty(fixtureRoot))
        {
            var qbRoot = Path.Combine(fixtureRoot, "qb");
            if (Directory.Exists(qbRoot)) qbwFiles.AddRange(SafeQbw(qbRoot, log));
            else log.Verbose($"qbblock: no fixture qb dir at {qbRoot}");
        }
        else
        {
            var dirs = installs
                .Select(i => i.DataDir)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Concat([
                    Path.Combine(@"C:\Users\Public\Public Documents", "Intuit"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Intuit"),
                ])
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var d in dirs) qbwFiles.AddRange(SafeQbw(d, log));
        }

        var capped = qbwFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
        log.Verbose($"qbblock: scanning {capped.Count} .qbw files (cap 20, found {qbwFiles.Count})");

        const long TlgLimit = 500L * 1024 * 1024;
        foreach (var f in capped)
        {
            if (f.StartsWith(@"\\", StringComparison.Ordinal))
            {
                string evidence = $"Company file on network path (NAS unsupported for multi-user): {f}.";
                log.Verbose($"qbblock: {evidence}");
                out_.Add(new Offender(
                    "QbBlockers", f, 60, 1.5, evidence, "Tier1",
                    "Move company file to local fixed drive; NAS is unsupported for multi-user"));
            }
            try
            {
                var tlg = f + ".tlg";
                if (File.Exists(tlg))
                {
                    long len = new FileInfo(tlg).Length;
                    if (len > TlgLimit)
                    {
                        string evidence = $"Sibling TLG {len / (1024.0 * 1024.0):F0}MB exceeds 500MB: {tlg}.";
                        log.Verbose($"qbblock: {evidence}");
                        out_.Add(new Offender(
                            "QbBlockers", f, 40, 1.0, evidence, "Tier1",
                            "Run manual backup with full verification to reset TLG"));
                    }
                    else log.Verbose($"qbblock: TLG ok ({len / 1024}KB) {tlg}");
                }
            }
            catch (Exception ex) { log.Verbose($"qbblock: TLG check skip {f}: {ex.Message}"); }
        }
    }

    static List<string> SafeQbw(string root, Logger log)
    {
        try { return Directory.GetFiles(root, "*.qbw", SearchOption.AllDirectories).ToList(); }
        catch (Exception ex) { log.Verbose($"qbblock: LIST-FAIL {root}: {ex.Message}"); return []; }
    }
}
