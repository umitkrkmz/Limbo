using Spectre.Console;

namespace Limbo.Console.Interactive;

internal static class HelpMenu
{
    // Comfortable reading length; full-width lines on a wide console are hard to follow.
    private const int MaxTextWidth = 100;

    private static readonly string[] Topics = ["quickstart", "modes", "profiles", "dns", "troubleshooting"];

    public static void Show()
    {
        while (true)
        {
            InteractiveUi.SafeClear();
            InteractiveUi.WriteHeader(Loc.T("help.subtitle"));

            var prompt = new SelectionPrompt<string>()
                .Title(Loc.T("help.prompt"))
                .HighlightStyle(new Style(Color.SteelBlue1))
                .AddChoices(Topics.Select(t => Loc.T($"help.topic.{t}")))
                .AddChoices(Loc.T("back"));

            var choice = AnsiConsole.Prompt(prompt);
            var topic = Topics.FirstOrDefault(t => Loc.T($"help.topic.{t}") == choice);
            if (topic == null) return;

            InteractiveUi.SafeClear();
            InteractiveUi.WriteHeader(Loc.T("help.subtitle"));
            var panel = new Panel(new Markup(Loc.T($"help.text.{topic}")))
                .Header($" {choice} ")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.SteelBlue1)
                .Padding(2, 1);
            panel.Width = Math.Min(AnsiConsole.Profile.Width, MaxTextWidth);
            AnsiConsole.Write(panel);
            InteractiveUi.PressEnterToContinue();
        }
    }
}
