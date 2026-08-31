using Spectre.Console;

namespace Limbo.Console.Interactive;

/// <summary>
/// Small rendering helpers shared by the interactive menu screens.
/// </summary>
internal static class InteractiveUi
{
    /// <summary>
    /// AnsiConsole.Clear() queries the console's cursor position, which
    /// throws IOException on some terminal emulators/piped setups that don't
    /// back a real console buffer (seen with piped input under Git Bash on
    /// Windows). Screen transitions should degrade to "no clear", not crash.
    /// </summary>
    public static void SafeClear()
    {
        try
        {
            AnsiConsole.Clear();
        }
        catch (IOException)
        {
            AnsiConsole.WriteLine();
        }
    }

    private const string Logo = """
          _      _           _
         | |    (_)         | |
         | |     _ _ __ ___ | |__   ___
         | |    | | '_ ` _ \| '_ \ / _ \
         | |____| | | | | | | |_) | (_) |
         |______|_|_| |_| |_|_.__/ \___/
        """;

    public static void WriteHeader(string subtitle)
    {
        var panel = new Panel(Logo)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.SteelBlue1)
            .Padding(2, 0);
        AnsiConsole.Write(panel);
        AnsiConsole.MarkupLine($"[bold]{subtitle.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>One-shot logo print for non-interactive CLI command output (no subtitle/status panel).</summary>
    public static void WriteHeaderNonInteractive()
    {
        var panel = new Panel(Logo)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.SteelBlue1)
            .Padding(2, 0);
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public static void PressEnterToContinue()
    {
        AnsiConsole.MarkupLine($"\n[grey]{Loc.T("press_enter")}[/]");
        System.Console.ReadLine();
    }
}
