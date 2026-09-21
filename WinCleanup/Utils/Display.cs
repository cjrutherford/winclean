using Spectre.Console;
using WinCleanup.Checks;

namespace WinCleanup.Utils;

// Display: Spectre.Console rendering + plain log-file mirror.
// Console gets color/tables/panels; the log file gets the same content as plain
// text so redirected pilot logs stay complete and readable.
public sealed class Display
{
    private readonly Logger _log;

    public Display(Logger log) => _log = log;

    public void Section(string title)
    {
        AnsiConsole.Write(new Rule($"[cyan bold]{Markup.Escape(title)}[/]").LeftJustified());
        _log.File($"=== {title} ===");
    }

    public void Line(string text)
    {
        AnsiConsole.MarkupLine(Markup.Escape(text));
        _log.File(text);
    }

    public void Fact(string label, string value)
    {
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(label)}[/] {Markup.Escape(value)}");
        _log.File($"{label} {value}");
    }

    public void Table(string[] headers, List<string[]> rows)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();
        foreach (var h in headers)
            table.AddColumn(new TableColumn($"[bold]{Markup.Escape(h)}[/]"));
        foreach (var r in rows)
        {
            var cells = new string[headers.Length];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = Markup.Escape(i < r.Length ? r[i] : "");
            table.AddRow(cells);
        }
        AnsiConsole.Write(table);
        _log.File(string.Join(" | ", headers));
        foreach (var r in rows) _log.File("  " + string.Join(" | ", r));
    }

    public void OffenderCard(int rank, Offender f)
    {
        Color border = f.Score >= 70 ? Color.Red : f.Score >= 40 ? Color.Yellow : Color.Green;
        var body = new Text(
            $"Why: {f.Evidence}" +
            (string.IsNullOrEmpty(f.FixHint) ? "" : $"\nFix: {f.FixHint}"));
        var panel = new Panel(body)
            .Header($"[bold]#{rank} {Markup.Escape(f.Name)}[/]  {Tui.Bar(f.Score)}")
            .HeaderAlignment(Justify.Left)
            .BorderColor(border)
            .Expand();
        AnsiConsole.Write(panel);
        _log.File($"  #{rank} [{f.Category}/{f.Tier}] score={f.Score}x{f.ConflictMultiplier} {f.Name} — {f.Evidence}");
        if (!string.IsNullOrEmpty(f.FixHint)) _log.File($"       fix: {f.FixHint}");
    }

    public void Box(string title, IEnumerable<string> lines)
    {
        var text = new Text(string.Join("\n", lines));
        var panel = new Panel(text)
            .Header($"[bold]{Markup.Escape(title)}[/]")
            .BorderColor(Color.Cyan)
            .Expand();
        AnsiConsole.Write(panel);
        _log.File($"=== {title} ===");
        foreach (var l in lines) _log.File(l);
    }
}
