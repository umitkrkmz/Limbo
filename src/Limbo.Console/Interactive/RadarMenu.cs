using Limbo.Core.Analysis;
using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Limbo.Core.Profiles;
using Limbo.Console.Commands;
using Spectre.Console;

namespace Limbo.Console.Interactive;

/// <summary>
/// Adaptive engine (Radar) on/off toggle, default-method configuration for
/// auto-detected domains, plus detected-domain list/import/export/clear.
/// </summary>
internal static class RadarMenu
{
    public static void Show(LimboConfig config)
    {
        while (true)
        {
            InteractiveUi.SafeClear();
            InteractiveUi.WriteHeader(Loc.T("radar.subtitle"));

            var blacklistManager = new BlacklistManager();
            var domains = blacklistManager.GetAllBlacklisted();

            AnsiConsole.MarkupLine($"{Loc.T("radar.status")} {(config.EnableRadar ? $"[green]{Loc.T("on")}[/]" : $"[grey]{Loc.T("off")}[/]")}");
            AnsiConsole.MarkupLine($"{Loc.T("radar.detected_count")} {domains.Count}");
            AnsiConsole.MarkupLine(Loc.T("radar.current_default", config.DefaultMethodProfile.Method));
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Loc.T("radar.prompt"))
                    .AddChoices(
                        config.EnableRadar ? Loc.T("radar.turn_off") : Loc.T("radar.turn_on"),
                        Loc.T("radar.default_method"),
                        Loc.T("radar.list"),
                        Loc.T("radar.import"),
                        Loc.T("radar.export"),
                        Loc.T("radar.clear"),
                        Loc.T("back")));

            if (choice == Loc.T("radar.turn_off"))
            {
                config.EnableRadar = false;
                ConfigManager.Save(config);
                AnsiConsole.MarkupLine($"[grey]{Loc.T("radar.turned_off")}[/]");
                InteractiveUi.PressEnterToContinue();
            }
            else if (choice == Loc.T("radar.turn_on"))
            {
                config.EnableRadar = true;
                ConfigManager.Save(config);
                AnsiConsole.MarkupLine($"[green]{Loc.T("radar.turned_on")}[/]");
                InteractiveUi.PressEnterToContinue();
            }
            else if (choice == Loc.T("radar.default_method"))
            {
                ChangeDefaultMethod(config);
            }
            else if (choice == Loc.T("radar.list"))
            {
                InteractiveUi.SafeClear();
                InteractiveUi.WriteHeader(Loc.T("radar.list_subtitle"));
                RadarCommands.Show();
                InteractiveUi.PressEnterToContinue();
            }
            else if (choice == Loc.T("radar.import"))
            {
                var importPath = AnsiConsole.Ask<string>(Loc.T("radar.import_prompt"));
                RadarCommands.Import(importPath);
                InteractiveUi.PressEnterToContinue();
            }
            else if (choice == Loc.T("radar.export"))
            {
                var exportPath = AnsiConsole.Ask<string>(Loc.T("radar.export_prompt"));
                RadarCommands.Export(exportPath);
                InteractiveUi.PressEnterToContinue();
            }
            else if (choice == Loc.T("radar.clear"))
            {
                RadarCommands.Clear();
                InteractiveUi.PressEnterToContinue();
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>
    /// Lets the user pick which bypass method Radar applies to newly
    /// auto-detected domains, and optionally configure that method's
    /// parameters manually instead of using adaptive defaults. Reuses the
    /// same method/parameter picker as the profile wizard.
    /// </summary>
    private static void ChangeDefaultMethod(LimboConfig config)
    {
        InteractiveUi.SafeClear();
        InteractiveUi.WriteHeader(Loc.T("radar.default_method_subtitle"));

        var template = config.DefaultMethodProfile;

        var method = AnsiConsole.Prompt(
            new SelectionPrompt<BypassMethod>()
                .Title(Loc.T("profiles.method_prompt"))
                .AddChoices(ProfileMenu.SelectableMethods));

        var useAdaptive = method == BypassMethod.Adaptive
            || AnsiConsole.Confirm(Loc.T("profiles.use_adaptive_prompt"), template.UseAdaptiveParams);

        // PromptMethodParameter operates on a DomainProfile - build one from
        // the current template, prompt if needed, then copy back.
        var working = new DomainProfile
        {
            Domain = "*",
            Method = method,
            UseAdaptiveParams = useAdaptive,
            FragmentSize = template.FragmentSize,
            SplitPosition = template.SplitPosition,
            SplitOffset = template.SplitOffset,
            FakeTtl = template.FakeTtl,
            EchPublicName = template.EchPublicName,
            FakePacketSize = template.FakePacketSize
        };

        if (!useAdaptive)
        {
            ProfileMenu.PromptMethodParameter(method, working);
        }

        config.DefaultMethodProfile = new ProfileConfig
        {
            Domain = "*",
            Method = working.Method,
            UseAdaptiveParams = working.UseAdaptiveParams,
            FragmentSize = working.FragmentSize,
            SplitPosition = working.SplitPosition,
            SplitOffset = working.SplitOffset,
            FakeTtl = working.FakeTtl,
            EchPublicName = working.EchPublicName,
            FakePacketSize = working.FakePacketSize
        };
        ConfigManager.Save(config);

        AnsiConsole.MarkupLine($"[green]{Loc.T("radar.default_method_saved")}[/]");
        InteractiveUi.PressEnterToContinue();
    }
}
