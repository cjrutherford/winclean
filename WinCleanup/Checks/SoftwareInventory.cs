using Microsoft.Win32;
using WinCleanup.Utils;

namespace WinCleanup.Checks;

// Software + configuration inventory. Read-only, never modifies.
// Sources: Uninstall registry (both hives/views), Startup Run keys + Startup
// folders, scheduled tasks (schtasks), services (sc). On fixture runs
// (--fixture-root) reads <root>/installed-apps.csv instead of the registry
// so Linux manual verification works.
public static class SoftwareInventory
{
    public record App(string Name, string Version, string Publisher, long SizeBytes, string Source);
    public record StartupEntry(string Scope, string Name, string Command);
    public record TaskEntry(string Name, string State, string Author);

    // Apps Intuit/QB depends on or that must never be auto-removed.
    static readonly string[] KeepIntact =
    [
        "QuickBooks", "Intuit", ".NET", "Visual C++", "SQL Server", "Microsoft 365",
        "Office", "Windows Driver", "Intel", "NVIDIA", "AMD", "Realtek", "Antivirus",
        "Defender", "Edge", "OneDrive",
    ];

    public static List<App> GetInstalledApps(Logger log, string fixtureRoot = "")
    {
        if (!string.IsNullOrEmpty(fixtureRoot))
        {
            var csv = Path.Combine(fixtureRoot, "installed-apps.csv");
            if (File.Exists(csv))
            {
                var list = new List<App>();
                foreach (var line in File.ReadAllLines(csv).Skip(1))
                {
                    var p = line.Split(',');
                    if (p.Length >= 3) list.Add(new App(p[0].Trim(), p[1].Trim(), p.Length > 2 ? p[2].Trim() : "", 0, "fixture"));
                }
                log.Verbose($"fixture apps: {list.Count} from {csv}");
                return list;
            }
            log.Verbose($"fixture apps: no installed-apps.csv at {csv}");
            return [];
        }
        var apps = new List<App>();
        if (!OperatingSystem.IsWindows()) return apps;
        if (OperatingSystem.IsWindows())
        {
        string[] keys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ];
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                foreach (var key in keys)
                {
                    try
                    {
                        using var b = RegistryKey.OpenBaseKey(hive, view);
                        using var k = b.OpenSubKey(key);
                        if (k == null) continue;
                        foreach (var sub in k.GetSubKeyNames())
                        {
                            try
                            {
                                using var s = k.OpenSubKey(sub);
                                if (s == null) continue;
                                var name = s.GetValue("DisplayName") as string;
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                if (s.GetValue("SystemComponent") is int sc && sc == 1) continue;
                                apps.Add(new App(
                                    name,
                                    s.GetValue("DisplayVersion") as string ?? "",
                                    s.GetValue("Publisher") as string ?? "",
                                    s.GetValue("EstimatedSize") is int kb ? (long)kb * 1024 : 0,
                                    $"{hive}/{view}"));
                            }
                            catch (Exception ex) { log.Verbose($"app subkey skip {sub}: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) { log.Verbose($"uninstall key skip {hive}/{view}/{key}: {ex.Message}"); }
                }
        }
        return apps.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(a => a.Name).ToList();
    }

    public static List<StartupEntry> GetStartupEntries(Logger log)
    {
        var list = new List<StartupEntry>();
        if (!OperatingSystem.IsWindows()) return list;
        if (OperatingSystem.IsWindows())
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var b = RegistryKey.OpenBaseKey(hive, view);
                    using var k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (k == null) continue;
                    foreach (var n in k.GetValueNames())
                        list.Add(new StartupEntry($"{hive}/{view}", n, (k.GetValue(n)?.ToString() ?? "")[..Math.Min(200, (k.GetValue(n)?.ToString() ?? "").Length)]));
                }
                catch (Exception ex) { log.Verbose($"run key skip: {ex.Message}"); }
            }
        foreach (var dir in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            }.Where(Directory.Exists))
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                    list.Add(new StartupEntry("StartupFolder", Path.GetFileName(f), f));
            }
            catch (Exception ex) { log.Verbose($"startup folder skip {dir}: {ex.Message}"); }
        }
        return list;
    }

    public static List<TaskEntry> GetScheduledTasks(Logger log)
    {
        var list = new List<TaskEntry>();
        if (!OperatingSystem.IsWindows()) return list;
        var csv = SysProbe.Run("schtasks", "/query /fo csv /v", timeoutMs: 30000, log: log);
        if (string.IsNullOrWhiteSpace(csv)) return list;
        foreach (var line in csv.Split('\n').Skip(1))
        {
            var cols = line.Split("\",\"");
            if (cols.Length < 4) continue;
            var name = cols[0].Trim('"', '\r', ' ');
            var state = cols[3].Trim('"', '\r', ' ');
            if (name.Contains('\\') && (state.Equals("Ready", StringComparison.OrdinalIgnoreCase) || state.Equals("Running", StringComparison.OrdinalIgnoreCase)))
                list.Add(new TaskEntry(name, state, ""));
        }
        return list;
    }

    public static void Report(Logger log, string fixtureRoot = "")
    {
        log.Info("=== SOFTWARE + CONFIG INVENTORY (read-only) ===");
        var apps = GetInstalledApps(log, fixtureRoot);
        log.Info($"installed apps: {apps.Count}");
        var big = apps.Where(a => a.SizeBytes > 0).OrderByDescending(a => a.SizeBytes).Take(15).ToList();
        foreach (var a in big)
            log.Verbose($"app size={(a.SizeBytes / 1e6):F0}MB ver={a.Version} pub={a.Publisher} :: {a.Name}");
        foreach (var a in big.Take(10))
            log.Info($"  big app: {a.SizeBytes / 1e6:F0}MB {a.Name} {a.Version}");
        var qb = apps.Where(a => a.Name.Contains("QuickBooks", StringComparison.OrdinalIgnoreCase)).ToList();
        log.Info(qb.Count == 0 ? "QuickBooks in Add/Remove: NOT FOUND (portable/store install?)" :
            $"QuickBooks entries: {string.Join("; ", qb.Select(a => a.Name + " " + a.Version))}");

        var startup = GetStartupEntries(log);
        log.Info($"startup entries: {startup.Count} (disable — never delete — to speed boot)");
        foreach (var e in startup.Take(20)) log.Info($"  startup [{e.Scope}] {e.Name}");

        var tasks = GetScheduledTasks(log);
        log.Info($"scheduled tasks (ready/running): {tasks.Count}");
        foreach (var t in tasks.Take(15)) log.Verbose($"task [{t.State}] {t.Name}");

        if (OperatingSystem.IsWindows())
        {
            var svc = SysProbe.Run("sc", "query state= all", timeoutMs: 30000, log: log);
            int running = svc.Split('\n').Count(l => l.Trim().Equals("STATE              : 4  RUNNING", StringComparison.Ordinal));
            log.Info($"services running: ~{running} (set unused non-Microsoft to Manual — never Disable QB/DB services)");
        }
        else log.Verbose("services skipped non-Windows");

        var protected_ = apps.Where(a => KeepIntact.Any(k => a.Name.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
        log.Info($"protected-from-touch: {protected_.Count} apps (QB, runtimes, drivers, AV) — report only, cleanup never removes these");
        log.Verbose($"inventory exit apps={apps.Count} startup={startup.Count} tasks={tasks.Count}");
    }
}
