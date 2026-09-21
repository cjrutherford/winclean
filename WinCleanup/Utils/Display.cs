using WinCleanup.Checks;

namespace WinCleanup.Utils;

// Display: pretty terminal rendering + plain log-file mirror.
// Console gets color/tables/boxes; the log file gets the same content as plain
// text so redirected pilot logs stay complete and readable.
public sealed class Display
{
    private readonly Logger _log;

    public Display(Logger log) => _log = log;

    public void Section(string title)
    {
        Tui.Rule();
        Console.WriteLine(Tui.Head(Tui.B($"  {title}")));
        Tui.Rule();
        _log.File($"=== {title} ===");
    }

    public void Line(string console, string? file = null)
    {
        Console.WriteLine(console);
        _log.File(Strip(file ?? console));
    }

    // Simple two-part line: label dimmed, value normal.
    public void Fact(string label, string value)
    {
        Console.WriteLine($"{Tui.Dimmed(label)} {value}");
        _log.File($"{label} {Strip(value)}");
    }

    public void Table(string[] headers, List<string[]> rows)
    {
        int w = Tui.Width();
        int cols = headers.Length;
        int[] widths = new int[cols];
        for (int c = 0; c < cols; c++)
            widths[c] = Math.Max(headers[c].Length, rows.Select(r => c < r.Length ? PlainLen(r[c]) : 0).DefaultIfEmpty(0).Max());
        int total = widths.Sum() + cols * 3 + 1;
        if (total > w)
        {
            int over = total - w;
            int last = cols - 1;
            widths[last] = Math.Max(20, widths[last] - over); // squeeze last column
        }

        string Sep() => "+" + string.Join("+", widths.Select(x => new string('-', x + 2))) + "+";
        void Row(string[] cells, bool header)
        {
            var wrapped = cells.Select((c, i) => Tui.Wrap(c, widths[i]).ToArray()).ToArray();
            int h = wrapped.Max(a => a.Length);
            for (int r = 0; r < h; r++)
            {
                var parts = new string[cols];
                for (int c = 0; c < cols; c++)
                {
                    string cell = r < wrapped[c].Length ? wrapped[c][r] : "";
                    parts[c] = " " + cell.PadRight(widths[c] + (cell.Length - PlainLen(cell))) + " ";
                }
                string line = "|" + string.Join("|", parts) + "|";
                Console.WriteLine(header ? Tui.B(line) : line);
            }
        }

        Console.WriteLine(Sep());
        Row(headers, header: true);
        Console.WriteLine(Sep());
        foreach (var r in rows) Row(r, header: false);
        Console.WriteLine(Sep());

        // Plain mirror for the log file.
        _log.File(string.Join(" | ", headers));
        foreach (var r in rows) _log.File("  " + string.Join(" | ", r.Select(Strip)));
    }

    public void OffenderCard(int rank, Offender f)
    {
        string head = $"{Tui.SeverityIcon(f.Score)} {Tui.B($"#{rank} {f.Name}")}  {Tui.Bar(f.Score)}  {Tui.Dimmed($"[{f.Category}/{f.Tier}]")}";
        Console.WriteLine(head);
        foreach (var l in Tui.Wrap($"Why: {f.Evidence}", Tui.Width() - 4))
            Console.WriteLine($"    {Tui.Dimmed(l)}");
        if (!string.IsNullOrEmpty(f.FixHint))
            foreach (var l in Tui.Wrap($"Fix: {f.FixHint}", Tui.Width() - 4))
                Console.WriteLine($"    {Tui.Ok(l)}");
        _log.File($"  #{rank} [{f.Category}/{f.Tier}] score={f.Score}x{f.ConflictMultiplier} {Strip(f.Name)} — {Strip(f.Evidence)}");
        if (!string.IsNullOrEmpty(f.FixHint)) _log.File($"       fix: {Strip(f.FixHint)}");
    }

    public void Box(string title, IEnumerable<string> lines)
    {
        int w = Tui.Width();
        string bar = new string('=', Math.Min(w, 60));
        Console.WriteLine(Tui.Head(bar));
        Console.WriteLine(Tui.Head(Tui.B($" {title}")));
        Console.WriteLine(Tui.Head(bar));
        _log.File($"=== {title} ===");
        foreach (var l in lines) { Console.WriteLine(l); _log.File(Strip(l)); }
        Console.WriteLine(Tui.Head(bar));
    }

    static string Strip(string s)
    {
        // Remove ANSI escapes for the log file.
        var sb = new System.Text.StringBuilder(s.Length);
        bool esc = false;
        foreach (char ch in s)
        {
            if (ch == '\u001b') { esc = true; continue; }
            if (esc) { if (ch == 'm') esc = false; continue; }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    static int PlainLen(string s) => Strip(s).Length;
}
