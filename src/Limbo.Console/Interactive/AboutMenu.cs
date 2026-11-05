using Spectre.Console;

namespace Limbo.Console.Interactive;

internal static class AboutMenu
{
    public static void Show()
    {
        while (true)
        {
            InteractiveUi.SafeClear();
            InteractiveUi.WriteHeader(Loc.T("about.subtitle"));

            var table = new Table().Border(TableBorder.Rounded).HideHeaders();
            table.AddColumn("Key");
            table.AddColumn("Value");
            table.AddRow(Loc.T("about.version"), AppInfo.VersionText);
            table.AddRow(Loc.T("about.license"), AppInfo.License);
            table.AddRow(Loc.T("about.source"), AppInfo.RepoUrl);
            table.AddRow(Loc.T("about.third_party"), Loc.T("about.third_party_value"));
            table.AddRow(Loc.T("about.updates"), FormatUpdateStatus());
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            var update = UpdateChecker.Available;
            var openRelease = update != null ? Loc.T("about.open_release", update.LatestTag) : null;

            var prompt = new SelectionPrompt<string>().Title(Loc.T("main_menu.prompt"));
            if (openRelease != null) prompt.AddChoice(openRelease);
            prompt.AddChoices(Loc.T("about.open_repo"), Loc.T("back"));

            var choice = AnsiConsole.Prompt(prompt);
            if (choice == openRelease)
            {
                Open(update!.ReleaseUrl);
            }
            else if (choice == Loc.T("about.open_repo"))
            {
                Open(AppInfo.RepoUrl);
            }
            else
            {
                return;
            }
        }
    }

    public static string FormatUpdateStatus() => UpdateChecker.Status switch
    {
        UpdateChecker.CheckStatus.Disabled => $"[grey]{Loc.T("update.disabled")}[/]",
        UpdateChecker.CheckStatus.Checking => $"[grey]{Loc.T("update.checking")}[/]",
        UpdateChecker.CheckStatus.Failed => $"[grey]{Loc.T("update.failed")}[/]",
        UpdateChecker.CheckStatus.UpdateAvailable => $"[yellow]{Loc.T("update.available", UpdateChecker.Available!.LatestTag.EscapeMarkup())}[/]",
        _ => $"[green]{Loc.T("update.up_to_date")}[/]"
    };

    private static void Open(string url)
    {
        var key = AppInfo.OpenInBrowser(url) ? "about.opened" : "about.open_failed";
        AnsiConsole.MarkupLine(Loc.T(key, url.EscapeMarkup()));
        InteractiveUi.PressEnterToContinue();
    }
}
