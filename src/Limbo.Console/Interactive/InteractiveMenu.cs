using Limbo.Core.Analysis;
using Limbo.Core.Config;
using Limbo.Core.Logging;
using Limbo.Core.Profiles;
using Limbo.Console.Commands;
using Spectre.Console;

namespace Limbo.Console.Interactive;

/// <summary>
/// Top-level interactive dashboard shown when `limbo` is launched with no
/// arguments. All argument-based CLI commands keep working unchanged
/// alongside this - see Program.cs.
/// </summary>
internal static class InteractiveMenu
{
    public static async Task<int> RunAsync(LimboConfig config, LimboLogger logger)
    {
        Loc.Current = config.Language;
        UpdateChecker.StartIfEnabled(config);

        while (true)
        {
            InteractiveUi.SafeClear();
            InteractiveUi.WriteHeader(Loc.T("main_menu.subtitle"), BuildStatusPanel(config));

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Loc.T("main_menu.prompt"))
                    .PageSize(10)
                    .AddChoices(
                        Loc.T("main_menu.start_engine"),
                        Loc.T("main_menu.profiles"),
                        Loc.T("main_menu.dns"),
                        Loc.T("main_menu.radar"),
                        Loc.T("main_menu.network_analysis"),
                        Loc.T("main_menu.calibrate"),
                        Loc.T("main_menu.test"),
                        Loc.T("main_menu.settings"),
                        Loc.T("main_menu.about"),
                        Loc.T("main_menu.exit")));

            if (choice == Loc.T("main_menu.start_engine"))
            {
                InteractiveUi.SafeClear();
                await RunCommand.RunAsync(config, logger);
                InteractiveUi.PressEnterToContinue();
            }
            else if (choice == Loc.T("main_menu.profiles"))
            {
                ProfileMenu.Show(config);
            }
            else if (choice == Loc.T("main_menu.dns"))
            {
                DnsMenu.Show(config);
            }
            else if (choice == Loc.T("main_menu.radar"))
            {
                RadarMenu.Show(config);
            }
            else if (choice == Loc.T("main_menu.network_analysis"))
            {
                await NetworkAnalysisMenu.ShowAsync();
            }
            else if (choice == Loc.T("main_menu.calibrate"))
            {
                await RunCalibration(config, logger);
            }
            else if (choice == Loc.T("main_menu.test"))
            {
                await RunConnectionTest(config);
            }
            else if (choice == Loc.T("main_menu.settings"))
            {
                ShowGeneralSettings(config);
            }
            else if (choice == Loc.T("main_menu.about"))
            {
                AboutMenu.Show();
            }
            else
            {
                return 0;
            }
        }
    }

    internal static Table BuildStatusPanel(LimboConfig config)
    {
        var isAdmin = SystemStatus.IsAdministrator();
        var netInfo = SystemStatus.GetPrimaryInterfaceDns();

        var profileManager = new ProfileManager();
        CommandHelpers.LoadProfilesFromConfig(profileManager, config);
        var profileCount = profileManager.GetAllProfiles().Count;
        var radarCount = new BlacklistManager().GetAllBlacklisted().Count;

        var interfaceText = netInfo?.Name ?? Loc.T("status.not_found");
        var dnsText = netInfo.HasValue && netInfo.Value.DnsServers.Count > 0
            ? string.Join(", ", netInfo.Value.DnsServers)
            : Loc.T("status.not_found");

        var table = new Table().Border(TableBorder.Rounded).HideHeaders();
        table.AddColumn("Key");
        table.AddColumn("Value");
        table.AddRow(Loc.T("status.admin"), isAdmin ? $"[green]{Loc.T("status.admin_yes")}[/]" : $"[red]{Loc.T("status.admin_no")}[/]");
        table.AddRow(Loc.T("status.interface"), interfaceText.EscapeMarkup());
        table.AddRow(Loc.T("status.system_dns"), dnsText.EscapeMarkup());
        table.AddRow(Loc.T("status.radar_engine"), config.EnableRadar ? $"[green]{Loc.T("status.on")}[/]" : $"[grey]{Loc.T("status.off")}[/]");
        table.AddRow(Loc.T("status.doh"), config.Dns.UseDoH ? $"[green]{Loc.T("status.on")}[/]" : $"[grey]{Loc.T("status.off")}[/]");
        table.AddRow(Loc.T("status.profile_count"), profileCount.ToString());
        table.AddRow(Loc.T("status.radar_count"), radarCount.ToString());
        if (UpdateChecker.Available is { } update)
        {
            table.AddRow(Loc.T("status.update"), $"[yellow]{Loc.T("status.update_value", update.LatestTag.EscapeMarkup())}[/]");
        }

        return table;
    }

    private static async Task RunCalibration(LimboConfig config, LimboLogger logger)
    {
        InteractiveUi.SafeClear();
        InteractiveUi.WriteHeader(Loc.T("calibrate.subtitle"));

        var domain = AnsiConsole.Prompt(new TextPrompt<string>(Loc.T("calibrate.domain_prompt")));
        var port = AnsiConsole.Prompt(new TextPrompt<int>(Loc.T("calibrate.port_prompt")).DefaultValue(443));

        var result = await CalibrateCommand.CalibrateAsync(config, logger, domain, port);
        if (result.Success && AnsiConsole.Confirm(Loc.T("calibrate.save_confirm", domain)))
        {
            CalibrateCommand.SaveAsProfile(config, domain, result);
        }

        InteractiveUi.PressEnterToContinue();
    }

    private static async Task RunConnectionTest(LimboConfig config)
    {
        InteractiveUi.SafeClear();
        InteractiveUi.WriteHeader(Loc.T("test.subtitle"));

        var domain = AnsiConsole.Prompt(new TextPrompt<string>(Loc.T("test.domain_prompt")));
        await TestCommand.RunAsync(config, domain);

        InteractiveUi.PressEnterToContinue();
    }

    private static void ShowGeneralSettings(LimboConfig config)
    {
        while (true)
        {
            InteractiveUi.SafeClear();
            InteractiveUi.WriteHeader(Loc.T("settings.subtitle"));

            var table = new Table().Border(TableBorder.Rounded).HideHeaders();
            table.AddColumn("Key");
            table.AddColumn("Value");
            table.AddRow(Loc.T("settings.filter"), config.Filter.EscapeMarkup());
            table.AddRow(Loc.T("settings.verbose"), config.Verbose ? $"[green]{Loc.T("on")}[/]" : $"[grey]{Loc.T("off")}[/]");
            table.AddRow(Loc.T("settings.log_file"), string.IsNullOrEmpty(config.LogFile) ? $"[grey]{Loc.T("settings.log_file_none")}[/]" : config.LogFile.EscapeMarkup());
            table.AddRow(Loc.T("settings.language"), config.Language.ToString());
            table.AddRow(Loc.T("settings.update_check"), config.CheckForUpdates ? $"[green]{Loc.T("on")}[/]" : $"[grey]{Loc.T("off")}[/]");
            table.AddRow(Loc.T("settings.config_file"), ConfigManager.GetPath(config).EscapeMarkup());
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Loc.T("settings.prompt"))
                    .AddChoices(
                        Loc.T("settings.toggle_verbose"), Loc.T("settings.set_log_file"),
                        Loc.T("settings.change_language"), Loc.T("settings.toggle_update_check"),
                        Loc.T("back")));

            if (choice == Loc.T("settings.toggle_verbose"))
            {
                config.Verbose = !config.Verbose;
                ConfigManager.Save(config);
            }
            else if (choice == Loc.T("settings.toggle_update_check"))
            {
                config.CheckForUpdates = !config.CheckForUpdates;
                ConfigManager.Save(config);
                if (config.CheckForUpdates) UpdateChecker.StartIfEnabled(config);
                else UpdateChecker.Stop();
            }
            else if (choice == Loc.T("settings.set_log_file"))
            {
                var path = AnsiConsole.Prompt(
                    new TextPrompt<string>(Loc.T("settings.log_file_prompt"))
                        .AllowEmpty()
                        .DefaultValue(config.LogFile ?? ""));
                config.LogFile = string.IsNullOrWhiteSpace(path) ? null : path;
                ConfigManager.Save(config);
            }
            else if (choice == Loc.T("settings.change_language"))
            {
                var language = AnsiConsole.Prompt(
                    new SelectionPrompt<Language>()
                        .Title(Loc.T("settings.language_prompt"))
                        .AddChoices(Language.English, Language.Turkish));
                config.Language = language;
                Loc.Current = language;
                ConfigManager.Save(config);
                AnsiConsole.MarkupLine($"[green]{Loc.T("settings.language_saved")}[/]");
                InteractiveUi.PressEnterToContinue();
            }
            else
            {
                return;
            }
        }
    }
}
