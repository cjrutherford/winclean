using Spectre.Console;

namespace WinCleanup.Utils;

// Picker menus, rendered by Spectre.Console prompts.
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
            var prompt = new MultiSelectionPrompt<string>()
                .Title(title)
                .NotRequired()
                .PageSize(12)
                .MoreChoicesText("[grey](Move up and down to reveal more choices)[/]")
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]");
            for (int i = 0; i < options.Count; i++)
            {
                string choice = options[i];
                if (!string.IsNullOrEmpty(hints[i]))
                    choice += $" [dim]({hints[i]})[/]";
                prompt.AddChoice(choice);
            }
            var picked = AnsiConsole.Prompt(prompt);
            return picked.Select(c => options.FindIndex(o => c.StartsWith(o, StringComparison.Ordinal))).Where(i => i >= 0).ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
        {
            return PickManyNumbered(title, options);
        }
    }

    static List<int> PickManyNumbered(string title, List<string> options)
    {
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(title)}[/]");
        for (int i = 0; i < options.Count; i++)
            AnsiConsole.MarkupLine($"  {i + 1}. {Markup.Escape(options[i])}");
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

    // Single-select menu. Returns index or -1 (cancelled/blank).
    public static int PickOne(string title, List<string> options)
    {
        try
        {
            if (!IsInteractive) return PickOneNumbered(title, options);
            var picked = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(title)
                    .PageSize(10)
                    .AddChoices(options));
            return options.IndexOf(picked);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
        {
            return PickOneNumbered(title, options);
        }
    }

    static int PickOneNumbered(string title, List<string> options)
    {
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(title)}[/]");
        for (int i = 0; i < options.Count; i++)
            AnsiConsole.MarkupLine($"  {i + 1}. {Markup.Escape(options[i])}");
        Console.Write("Enter number (blank cancels): ");
        string? input = Console.ReadLine();
        if (int.TryParse((input ?? "").Trim(), out int n) && n >= 1 && n <= options.Count)
            return n - 1;
        return -1;
    }
}
