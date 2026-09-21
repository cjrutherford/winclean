using WinCleanup.Models;
using WinCleanup.Utils;

namespace WinCleanup.Checks;

// Startup impact analysis: ranks autoruns by boot cost and flags non-essential
// high-cost entries. Run-key hits become Tier-0 offenders using Mitigate's
// `startup::<HKLM|HKCU>::<64|32>::<name>` naming; Startup-folder hits are Tier-1
// (delete the shortcut manually). AV / QuickBooks / drivers / Windows core are
// never flagged.
public static class StartupAnalyzer
{
    static readonly string[] Heavy =
    [
        "Teams", "OneDrive", "Spotify", "Steam", "Discord", "Slack", "Zoom",
        "Adobe", "Apple", "iTunesHelper", "GoogleDrive", "Dropbox", "Skype",
        "Cortana", "Xbox", "EA Desktop", "EpicGames", "Riot ",
    ];

    static readonly string[] Medium =
    [
        "Update", "Updater", "Helper", "Launcher", "Tray", "Agent", "Monitor",
        "OneDriveSetup", "EdgeUpdate", "Chrome",
    ];

    // Never flag: security, QuickBooks/hosting stack, drivers, Windows core.
    static readonly string[] Protected =
    [
        "Norton", "McAfee", "Trend Micro", "Avast", "AVG", "Kaspersky",
        "Bitdefender", "ESET", "Sophos", "Malwarebytes", "Defender",
        "SecurityHealth", "QBDBMgr", "QBCFMonitor", "QuickBooks", "QBUpdate",
        "Realtek", "Intel", "NVIDIA", "AMD", "Synaptics", "ELAN",
        "Windows Defender", "dismhost", "ctfmon",
    ];

    public static List<Offender> Analyze(Logger log, CleanupOptions o)
    {
        var found = new List<Offender>();
        var entries = SoftwareInventory.GetStartupEntries(log, o.FixtureRoot);
        log.Verbose($"startup-analysis: {entries.Count} entries");
        if (entries.Count == 0) return found;

        foreach (var e in entries)
        {
            string hay = $"{e.Name} {e.Command}";
            if (Protected.Any(p => hay.Contains(p, StringComparison.OrdinalIgnoreCase)))
            { log.Verbose($"startup: protected (never flag) [{e.Scope}] {e.Name}"); continue; }

            bool heavy = Heavy.Any(h => hay.Contains(h, StringComparison.OrdinalIgnoreCase));
            bool med = !heavy && Medium.Any(m => hay.Contains(m, StringComparison.OrdinalIgnoreCase));
            if (!heavy && !med) { log.Verbose($"startup: low impact [{e.Scope}] {e.Name}"); continue; }

            int score = heavy ? 55 : 30;
            string impact = heavy ? "high boot cost" : "medium boot cost";
            if (e.Scope.StartsWith("StartupFolder", StringComparison.OrdinalIgnoreCase))
            {
                string evidence = $"Startup-folder shortcut '{e.Name}' has {impact}.";
                log.Verbose($"startup: {evidence}");
                found.Add(new Offender("StartupImpact", e.Name, score, 1.0, evidence, "Tier1",
                    $"Delete the shortcut: {e.Command} (files stay installed)"));
            }
            else
            {
                // Scope looks like "LocalMachine/Registry64" — map to Mitigate naming.
                string hive = e.Scope.Contains("LocalMachine", StringComparison.OrdinalIgnoreCase) ? "HKLM" : "HKCU";
                string view = e.Scope.Contains("Registry64", StringComparison.OrdinalIgnoreCase) ? "64" : "32";
                string offenderName = $"startup::{hive}::{view}::{e.Name}";
                string evidence = $"Run value '{e.Name}' [{hive}/{view}] has {impact}: {Trunc(e.Command, 120)}.";
                log.Verbose($"startup: {evidence}");
                found.Add(new Offender("StartupImpact", offenderName, score, 1.0, evidence, "Tier0",
                    $"Disable (backed up first): --offenders --mitigate [--dry-run]"));
            }
        }
        log.Info($"startup analysis: {entries.Count} entries, {found.Count} flagged (high/medium, non-essential)");
        return found;
    }

    static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
