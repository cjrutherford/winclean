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

    public void Print(Display d, string logPath)
    {
        var lines = new List<string>
        {
            string.IsNullOrEmpty(_mode) ? "WinCleanup run" : $"Mode: {_mode}",
            "",
        };
        if (_findings.Count > 0)
        {
            lines.Add($"Key findings ({_findings.Count}):");
            lines.AddRange(_findings.Select(f => $"  - {f}"));
        }
        else lines.Add("Key findings: none");
        lines.Add("");
        if (_actions.Count > 0)
        {
            lines.Add("Actions taken:");
            lines.AddRange(_actions.Select(a => $"  - {a}"));
        }
        else lines.Add("Actions taken: none (read-only run)");
        if (_next.Count > 0)
        {
            lines.Add("");
            lines.Add("Suggested next steps:");
            lines.AddRange(_next.Select((s, i) => $"  {i + 1}. {s}"));
        }
        lines.Add("");
        lines.Add($"Full verbose log: {logPath}");
        d.Box("SUMMARY", lines);
    }

    // Legacy plain path (kept for non-display flows).
    public void Print(Logger log, string logPath) => Print(new Display(log), logPath);
}
