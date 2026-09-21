using WinCleanup.Checks;

namespace WinCleanup.Utils;

// End-of-run summary: a short, always-on-console block so a run is followable
// without reading the full verbose log. Sections appear only when non-empty.
public sealed class Summary
{
    private readonly List<string> _findings = new();
    private readonly List<string> _actions = new();
    private readonly List<string> _next = new();
    private string _mode = "";

    public void Mode(string m) => _mode = m;
    public void Finding(string s) => _findings.Add(s);
    public void Action(string s) => _actions.Add(s);
    public void Next(string s) => _next.Add(s);

    public void TopOffenders(List<Offender> offenders, int n = 3)
    {
        foreach (var f in offenders.Take(n))
            _findings.Add($"#{_findings.Count + 1} [{f.Category}] {f.Name} (score {f.Score}x{f.ConflictMultiplier}, {f.Tier})");
        if (offenders.Count > n)
            _findings.Add($"…plus {offenders.Count - n} more (see log)");
    }

    public void Print(Logger log, string logPath)
    {
        log.Info("");
        log.Info("================ SUMMARY ================");
        if (!string.IsNullOrEmpty(_mode)) log.Info($"Mode: {_mode}");
        if (_findings.Count > 0)
        {
            log.Info($"Key findings ({_findings.Count}):");
            foreach (var f in _findings) log.Info($"  - {f}");
        }
        else log.Info("Key findings: none");
        if (_actions.Count > 0)
        {
            log.Info("Actions taken:");
            foreach (var a in _actions) log.Info($"  - {a}");
        }
        else log.Info("Actions taken: none (read-only run)");
        if (_next.Count > 0)
        {
            log.Info("Suggested next steps:");
            foreach (var s in _next) log.Info($"  - {s}");
        }
        log.Info($"Full verbose log: {logPath}");
        log.Info("=========================================");
    }
}
