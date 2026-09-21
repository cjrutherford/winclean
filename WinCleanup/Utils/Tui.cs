namespace WinCleanup.Utils;

// Terminal display primitives: colors, tables, score bars, boxes.
// Auto-plain when output is redirected, NO_COLOR is set, or --no-color is passed,
// so piped pilot logs stay clean. BCL only, no cursor tricks except in Menu.
public static class Tui
{
    public static bool ColorEnabled { get; set; } = !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    const string Reset = "\u001b[0m";
    const string Bold = "\u001b[1m";
    const string Dim = "\u001b[2m";
    const string Red = "\u001b[31m";
    const string Green = "\u001b[32m";
    const string Yellow = "\u001b[33m";
    const string Blue = "\u001b[34m";
    const string Cyan = "\u001b[36m";

    static bool Unicode()
    {
        try { return Console.OutputEncoding.CodePage == 65001; }
        catch { return false; }
    }

    public static string C(string text, string code) => ColorEnabled ? code + text + Reset : text;
    public static string B(string t) => C(t, Bold);
    public static string Dimmed(string t) => C(t, Dim);
    public static string Ok(string t) => C(t, Green);
    public static string WarnC(string t) => C(t, Yellow);
    public static string Crit(string t) => C(t, Red);
    public static string Head(string t) => C(t, Cyan);

    public static string SeverityIcon(int score) =>
        score >= 70 ? Crit(Unicode() ? "\u2716" : "[XX]") :
        score >= 40 ? WarnC(Unicode() ? "\u26A0" : "[!!]") :
                      Ok(Unicode() ? "\u2713" : "[OK]");

    public static int Width()
    {
        try { return Math.Max(60, Math.Min(140, Console.WindowWidth)); }
        catch { return 100; }
    }

    public static void Rule()
    {
        Console.WriteLine(ColorEnabled
            ? Dim + new string(Unicode() ? '─' : '-', Width()) + Reset
            : new string('-', Width()));
    }

    // 10-cell score bar: ████████░░ 80
    public static string Bar(int score)
    {
        int filled = Math.Clamp(score / 10, 0, 10);
        string cells = Unicode()
            ? new string('█', filled) + new string('░', 10 - filled)
            : new string('#', filled) + new string('-', 10 - filled);
        string colored = score >= 70 ? Crit(cells) : score >= 40 ? WarnC(cells) : Ok(cells);
        return $"{colored} {score,3}";
    }

    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var para in (text ?? "").Split('\n'))
        {
            var words = para.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { lines.Add(""); continue; }
            var cur = "";
            foreach (var w in words)
            {
                if (cur.Length == 0) cur = w;
                else if (cur.Length + 1 + w.Length <= width) cur += " " + w;
                else { lines.Add(cur); cur = w; }
            }
            lines.Add(cur);
        }
        return lines;
    }
}
