using System.Text.RegularExpressions;
using WinCleanup.Cleaners;
using WinCleanup.Utils;

namespace WinCleanup.Checks;

// QuickBooks firewall check: static per-year TCP ports + missing-rule offenders.
// 2019+ use dynamic ports (only 8019 is static); the live range is readable in
// Database Server Manager > Port Monitor. Fixture/non-Windows runs only get
// verbose lines (OffenderScan covers fixture QB checks); offenders only on Windows.
public static class QbFirewall
{
    public static int YearFromVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return 0;
        var m = Regex.Match(version.Trim(), @"(\d{2,4})(?:\.\d+)?\s*$");
        if (!m.Success)
            return 0;
        var digits = m.Groups[1].Value;
        if (digits.Length == 4)
        {
            if (int.TryParse(digits, out int full) && full >= 2000 && full <= 2099)
                return full;
            return 0;
        }
        if (digits.Length == 2)
        {
            if (int.TryParse(digits, out int shortYear))
                return 2000 + shortYear;
            return 0;
        }
        return 0;
    }

    // Static per-year TCP ports (Intuit docs). 2019+ (incl. 2024/2025) use dynamic
    // ports: only 8019 is static; the live port comes from
    // Database Server Manager > Port Monitor (renewable; rescan folders after).
    public static int[] StaticPorts(int year) => year switch
    {
        2013 => new[] { 8019, 56723, 55353, 55354, 55355, 55356, 55357 },
        2014 => new[] { 8019, 56724, 55358, 55359, 55360, 55361, 55362 },
        2015 => new[] { 8019, 56725, 55363, 55364, 55365, 55366, 55367 },
        2016 => new[] { 8019, 56726, 55368, 55369, 55370, 55371, 55372 },
        2017 => new[] { 8019, 56727, 55373, 55374, 55375, 55376, 55377 },
        2018 => new[] { 8019, 56728, 55378, 55379, 55380, 55381, 55382 },
        >= 2019 => new[] { 8019 },
        _ => new[] { 8019 },
    };

    public static bool UsesDynamicPorts(int year) => year >= 2019 || year == 0;

    public static bool HasInboundRule(Logger log, int port)
    {
        log.Verbose($"firewall: checking inbound rule for TCP {port}");
        if (!OperatingSystem.IsWindows())
        {
            log.Verbose($"firewall: TCP {port} rule check skipped non-Windows");
            return false;
        }
        var output = SysProbe.Run("netsh", "advfirewall firewall show rule name=all", log: log);
        log.Verbose($"firewall: netsh output {output.Length}B scanned for port {port}");
        bool found = output.Contains(port.ToString(), StringComparison.Ordinal);
        log.Verbose($"firewall: TCP {port} inbound rule {(found ? "found" : "missing")}");
        return found;
    }

    public static List<Offender> Check(Logger log, List<QuickBooksCleaner.Install> installs)
    {
        var offenders = new List<Offender>();
        installs ??= new List<QuickBooksCleaner.Install>();
        if (installs.Count == 0)
        {
            log.Verbose("firewall: no QB installs — nothing to check");
            return offenders;
        }
        if (!OperatingSystem.IsWindows())
        {
            foreach (var inst in installs)
                log.Verbose($"firewall: QB {inst.Version} skipped non-Windows/fixture (OffenderScan covers fixture QB checks)");
            return offenders;
        }
        foreach (var inst in installs)
        {
            int year = YearFromVersion(inst.Version);
            int[] ports = StaticPorts(year);
            log.Verbose($"firewall: QB {inst.Version} year={year} ports=[{string.Join(",", ports)}]");
            foreach (var port in ports)
            {
                if (HasInboundRule(log, port))
                {
                    log.Verbose($"firewall: QB {inst.Version} TCP {port} rule present");
                    continue;
                }
                string evidence = $"No inbound firewall rule found for QuickBooks {inst.Version} TCP port {port}; multi-user hosting may be blocked.";
                string fixHint = $"netsh advfirewall firewall add rule name=\"QuickBooks {inst.Version} inbound\" dir=in action=allow protocol=TCP localport={port}";
                log.Verbose($"firewall: {evidence}");
                offenders.Add(new Offender(
                    "QbBlockers", $"QB {inst.Version} TCP {port}", 65, 1.5, evidence, "Tier1", fixHint));
            }
        }
        return offenders;
    }

    public static void PrintRules(Logger log, List<QuickBooksCleaner.Install> installs)
    {
        installs ??= new List<QuickBooksCleaner.Install>();
        if (installs.Count == 0)
        {
            log.Info("QuickBooks firewall: no installs — nothing to print");
            return;
        }
        foreach (var inst in installs)
        {
            int year = YearFromVersion(inst.Version);
            string csv = string.Join(",", StaticPorts(year));
            log.Verbose($"firewall: print rules QB {inst.Version} year={year} ports=[{csv}]");
            log.Info($"netsh advfirewall firewall add rule name=\"QuickBooks {inst.Version} inbound\" dir=in action=allow protocol=TCP localport={csv}");
            log.Info($"netsh advfirewall firewall add rule name=\"QuickBooks {inst.Version} outbound\" dir=out action=allow protocol=TCP localport={csv}");
            if (UsesDynamicPorts(year))
                log.Info($"note: QuickBooks {inst.Version} ({(year == 0 ? "unknown year" : year.ToString())}) uses dynamic ports (2019+, incl. 2024/2025): allow 8019 PLUS the live port from Database Server Manager > Port Monitor tab (Renew there, then Scan Folders > Scan Now to reset permissions)");
            if (!string.IsNullOrWhiteSpace(inst.ProgramDir))
            {
                foreach (var exe in new[] { "QBW32.exe", "QBDBMgrN.exe", "QBCFMonitorService.exe", "QBUpdate.exe" })
                {
                    string program = Path.Combine(inst.ProgramDir, exe);
                    log.Info($"netsh advfirewall firewall add rule name=\"QuickBooks {inst.Version} {exe} inbound\" dir=in action=allow program=\"{program}\" enable=yes");
                    log.Info($"netsh advfirewall firewall add rule name=\"QuickBooks {inst.Version} {exe} outbound\" dir=out action=allow program=\"{program}\" enable=yes");
                }
            }
            else
            {
                log.Verbose($"firewall: QB {inst.Version} has no ProgramDir — skip program rules");
            }
        }
        log.Info("note: 2019+ (incl. 2024/2025) use dynamic ports — 8019 plus the live Port Monitor port; renew + rescan folders after changing");
    }
}
