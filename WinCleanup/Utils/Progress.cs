namespace WinCleanup.Utils;

// Stage plan + progress reporting. Everything goes through Logger so it lands
// in both console and the log file (no cursor tricks — stays readable in logs).
public sealed class StagePlan
{
    private readonly Logger _log;
    private readonly List<string> _stages;
    private int _i;

    public StagePlan(Logger log, IEnumerable<string> stages)
    {
        _log = log;
        _stages = stages.ToList();
        _i = 0;
        _log.Info($"Plan: {string.Join(" → ", _stages.Select((s, k) => $"[{k + 1}/{_stages.Count}] {s}"))}");
    }

    public void Begin(string name)
    {
        _i++;
        _log.Info($"--- [{_i}/{_stages.Count}] {name} ---");
    }
}
