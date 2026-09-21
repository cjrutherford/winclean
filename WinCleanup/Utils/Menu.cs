namespace WinCleanup.Utils;

// Interactive picker menus (arrow keys + space + enter).
// Falls back to numbered input when the console is redirected or keys are
// unavailable, and reports non-interactive so callers can use typed confirm.
public static class Menu
{
    public static bool IsInteractive =>
        Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;

    // Multi-select checklist. Returns selected indices (empty = none).
    public static List<int> PickMany(string title, List<string> options, List<string>? hints = null)
    {
        hints ??= options.Select(_ => "").ToList();
        try
        {
            if (!IsInteractive) return PickManyNumbered(title, options);
            return PickManyKeys(title, options, hints);
        }
        catch (InvalidOperationException)
        {
            return PickManyNumbered(title, options);
        }
    }

    static List<int> PickManyKeys(string title, List<string> options, List<string> hints)
    {
        var picked = new bool[options.Count];
        int cursor = 0;
        int w = Tui.Width();
        while (true)
        {
            Console.Clear();
            Console.WriteLine(Tui.Head(Tui.B(title)));
            Console.WriteLine(Tui.Dimmed("  ↑/↓ move · space toggle · a all · n none · enter apply · esc cancel"));
            Tui.Rule();
            for (int i = 0; i < options.Count; i++)
            {
                string box = picked[i] ? Tui.Ok("[x]") : "[ ]";
                string line = $"  {(i == cursor ? Tui.B(">") : " ")} {box} {options[i]}";
                if (line.Length > w) line = line[..w];
                if (i == cursor) Console.WriteLine(Tui.B(line));
                else Console.WriteLine(line);
                if (!string.IsNullOrEmpty(hints[i]))
                    foreach (var h in Tui.Wrap(hints[i], w - 8))
                        Console.WriteLine(Tui.Dimmed($"        {h}"));
            }
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.UpArrow) cursor = (cursor - 1 + options.Count) % options.Count;
            else if (key.Key == ConsoleKey.DownArrow) cursor = (cursor + 1) % options.Count;
            else if (key.Key == ConsoleKey.Spacebar) picked[cursor] = !picked[cursor];
            else if (key.KeyChar is 'a' or 'A') picked = picked.Select(_ => true).ToArray();
            else if (key.KeyChar is 'n' or 'N') picked = picked.Select(_ => false).ToArray();
            else if (key.Key == ConsoleKey.Enter)
            {
                Console.Clear();
                return picked.Select((p, i) => (p, i)).Where(t => t.p).Select(t => t.i).ToList();
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                Console.Clear();
                return [];
            }
        }
    }

    // Single-select menu. Returns index or -1 (cancelled/blank).
    public static int PickOne(string title, List<string> options)
    {
        try
        {
            if (!IsInteractive) return PickOneNumbered(title, options);
            Console.Clear();
            Console.WriteLine(Tui.Head(Tui.B(title)));
            Console.WriteLine(Tui.Dimmed("  ↑/↓ move · enter select · esc cancel"));
            Tui.Rule();
            int cursor = 0;
            while (true)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    string line = $"  {(i == cursor ? Tui.B(">") : " ")} {options[i]}";
                    if (i == cursor) Console.WriteLine(Tui.B(line));
                    else Console.WriteLine(line);
                }
                var key = Console.ReadKey(intercept: true);
                // Redraw options block.
                try
                {
                    Console.SetCursorPosition(0, Console.CursorTop - options.Count);
                    for (int i = 0; i < options.Count; i++)
                        Console.WriteLine(new string(' ', Tui.Width()));
                    Console.SetCursorPosition(0, Console.CursorTop - options.Count);
                }
                catch { Console.Clear(); }
                if (key.Key == ConsoleKey.UpArrow) cursor = (cursor - 1 + options.Count) % options.Count;
                else if (key.Key == ConsoleKey.DownArrow) cursor = (cursor + 1) % options.Count;
                else if (key.Key == ConsoleKey.Enter) return cursor;
                else if (key.Key == ConsoleKey.Escape) return -1;
            }
        }
        catch (InvalidOperationException)
        {
            return PickOneNumbered(title, options);
        }
    }

    static int PickOneNumbered(string title, List<string> options)
    {
        Console.WriteLine(Tui.B(title));
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($"  {i + 1}. {options[i]}");
        Console.Write("Enter number (blank cancels): ");
        string? input = Console.ReadLine();
        if (int.TryParse((input ?? "").Trim(), out int n) && n >= 1 && n <= options.Count)
            return n - 1;
        return -1;
    }
    static List<int> PickManyNumbered(string title, List<string> options)
    {
        Console.WriteLine(Tui.B(title));
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($"  {i + 1}. {options[i]}");
        Console.Write("Enter numbers (comma-separated), 'all', or blank for none: ");
        string? input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) return [];
        if (input.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(0, options.Count).ToList();
        var picked = new List<int>();
        foreach (var part in input.Split(','))
            if (int.TryParse(part.Trim(), out int n) && n >= 1 && n <= options.Count && !picked.Contains(n - 1))
                picked.Add(n - 1);
        return picked;
    }
}
