using Spectre.Console;

namespace WinCleanup.Utils;

// Terminal display primitives, rendered by Spectre.Console.
// Color mode: auto-plain when redirected or NO_COLOR is set; --color/--no-color
// override. Log-file mirrors stay plain text (see Display).
public static class Tui
{
    public static bool ColorEnabled { get; set; } = !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static void ApplyColorMode()
    {
        if (!ColorEnabled)
            AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
    }

    public static string SeverityColor(int score) =>
        score >= 70 ? "red" : score >= 40 ? "yellow" : "green";

    public static string SeverityWord(int score) =>
        score >= 70 ? "critical" : score >= 40 ? "warning" : "ok";

    // 10-cell score bar, markup-escaped already (block chars need no escaping).
    public static string Bar(int score)
    {
        int filled = Math.Clamp(score / 10, 0, 10);
        string cells = new string('█', filled) + new string('░', 10 - filled);
        return $"[{SeverityColor(score)}]{cells}[/] {score,3}";
    }

    // Plain-text bar for the log file.
    public static string BarPlain(int score) =>
        $"[{new string('#', Math.Clamp(score / 10, 0, 10)).PadRight(10, '-')} {score,3}]";

    public static string Strip(string s)
    {
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
}
