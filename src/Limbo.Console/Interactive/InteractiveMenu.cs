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

            var modeText = config.GlobalMode ? Loc.T("status.mode_global") : Loc.T("status.mode_profiles");
            var otherModeText = config.GlobalMode ? Loc.T("status.mode_profiles") : Loc.T("status.mode_global");

            var actions = new Dictionary<string, Func<Task<bool>>>();
            string Item(string icon, string label, Func<Task<bool>> action)
            {
                var text = $"{icon}  {label}";
                actions[text] = action;
                return text;
            }
            Func<Task<bool>> Run(Action action) => () => { action(); return Task.FromResult(true); };

            var prompt = new SelectionPrompt<string>()
                .Title(Loc.T("main_menu.prompt"))
                .Mode(SelectionMode.Leaf)
                .PageSize(20)
                .HighlightStyle(new Style(Color.SteelBlue1))
                .AddChoiceGroup($"[grey]{Loc.T("main_menu.group_engine")}[/]",
                    Item("▶", $"{Loc.T("main_menu.start_engine")}  [grey]({modeText.EscapeMarkup()})[/]", async () =>
                    {
                        InteractiveUi.SafeClear();
                        await RunCommand.RunAsync(config, logger);
                        InteractiveUi.PressEnterToContinue();
                        return true;
                    }),
                    Item("⇄", Loc.T("main_menu.switch_mode", otherModeText.EscapeMarkup()), Run(() =>
                    {
                        config.GlobalMode = !config.GlobalMode;
                        ConfigManager.Save(config);
                    })))
                .AddChoiceGroup($"[grey]{Loc.T("main_menu.group_config")}[/]",
                    Item("◆", Loc.T("main_menu.profiles"), Run(() => ProfileMenu.Show(config))),
                    Item("◇", Loc.T("main_menu.dns"), Run(() => DnsMenu.Show(config))),
                    Item("◎", Loc.T("main_menu.radar"), Run(() => RadarMenu.Show(config))),
                    Item("⚙", Loc.T("main_menu.settings"), Run(() => ShowGeneralSettings(config))))
                .AddChoiceGroup($"[grey]{Loc.T("main_menu.group_tools")}[/]",
                    Item("◐", Loc.T("main_menu.network_analysis"), async () => { await NetworkAnalysisMenu.ShowAsync(); return true; }),
                    Item("↻", Loc.T("main_menu.calibrate"), async () => { await RunCalibration(config, logger); return true; }),
                    Item("⇆", Loc.T("main_menu.test"), async () => { await RunConnectionTest(config); return true; }))
                .AddChoiceGroup("[grey]LIMBO[/]",
                    Item("?", Loc.T("main_menu.help"), Run(HelpMenu.Show)),
                    Item("ℹ", Loc.T("main_menu.about"), Run(AboutMenu.Show)),
                    Item("✕", Loc.T("main_menu.exit"), () => Task.FromResult(false)));

            var keepGoing = await actions[AnsiConsole.Prompt(prompt)]();
            if (!keepGoing) return 0;
        }
    }

    // Logo panel is 39 columns wide; the status panel next to it gets the
    // golden-ratio share (39 x 1.618 = 63) so the eye lands on the status first.
    private const int LogoPanelWidth = 39;
    private const int StatusPanelWidth = 63;

    internal static Table BuildStatusPanel(LimboConfig config)
    {
        var isAdmin = SystemStatus.IsAdministrator();
        var netInfo = SystemStatus.GetPrimaryInterfaceDns();

        var profileManager = new ProfileManager();
        CommandHelpers.LoadProfilesFromConfig(profileManager, config);
        var profileCount = profileManager.GetAllProfiles().Count;
        var radarCount = new BlacklistManager().GetAllBlacklisted().Count;

        var interfaceText = netInfo?.Name ?? Loc.T("status.not_found");
        var (dnsV4, dnsV6) = SystemStatus.SplitByFamily(netInfo?.DnsServers ?? []);
        var dnsServers = dnsV4.Concat(dnsV6).ToList();
        var dnsText = dnsServers.Count > 0 ? string.Join(", ", dnsServers) : Loc.T("status.not_found");
        var dohHost = Uri.TryCreate(config.Dns.DoHServer, UriKind.Absolute, out var dohUri) ? dohUri.Host : config.Dns.DoHServer;

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.SteelBlue1).HideHeaders();
        table.AddColumn("Key");
        table.AddColumn("Value");
        var availableWidth = AnsiConsole.Profile.Width - LogoPanelWidth - 1;
        if (availableWidth >= 40)
            table.Width(Math.Min(StatusPanelWidth, availableWidth));

        table.AddRow(Loc.T("status.mode"), RadarMenu.FormatMode(config));
        table.AddRow(Loc.T("status.admin"), isAdmin ? $"[green]{Loc.T("status.admin_yes")}[/]" : $"[red]{Loc.T("status.admin_no")}[/]");
        table.AddRow(Loc.T("status.doh"), config.Dns.UseDoH
            ? $"[green]{Loc.T("status.on")}[/] [grey]· {dohHost.EscapeMarkup()}[/]"
            : $"[grey]{Loc.T("status.off")}[/]");
        table.AddRow(Loc.T("status.network"), $"{interfaceText.EscapeMarkup()} [grey]·[/] {dnsText.EscapeMarkup()}");
        table.AddRow(Loc.T("status.profiles_radar"), Loc.T(
            "status.profiles_radar_value",
            profileCount,
            radarCount,
            config.EnableRadar ? $"[green]{Loc.T("status.on")}[/]" : $"[grey]{Loc.T("status.off")}[/]"));
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
