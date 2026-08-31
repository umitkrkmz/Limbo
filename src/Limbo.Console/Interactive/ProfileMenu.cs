using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Limbo.Core.Profiles;
using Limbo.Console.Commands;
using Spectre.Console;

namespace Limbo.Console.Interactive;

/// <summary>
/// Interactive profile list / add / edit / delete wizard.
/// The method/parameter picker is also reused by RadarMenu for the
/// "default method for auto-detected domains" screen.
/// </summary>
internal static class ProfileMenu
{
    internal static readonly BypassMethod[] SelectableMethods =
    [
        BypassMethod.Adaptive, BypassMethod.Ech, BypassMethod.TcpFragmentation,
        BypassMethod.SmartFragmentation, BypassMethod.Desync, BypassMethod.FakeTtl,
        BypassMethod.UdpFake, BypassMethod.QuicFake
    ];

    public static void Show(LimboConfig config)
    {
        var profileManager = new ProfileManager();
        CommandHelpers.LoadProfilesFromConfig(profileManager, config);

        while (true)
        {
            InteractiveUi.SafeClear();
            InteractiveUi.WriteHeader(Loc.T("profiles.subtitle"));

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Loc.T("profiles.prompt"))
                    .AddChoices(
                        Loc.T("profiles.list"), Loc.T("profiles.add"),
                        Loc.T("profiles.edit"), Loc.T("profiles.delete"), Loc.T("back")));

            if (choice == Loc.T("profiles.list"))
            {
                InteractiveUi.SafeClear();
                InteractiveUi.WriteHeader(Loc.T("profiles.list_subtitle"));
                ConfigCommands.ListProfiles(profileManager);
                InteractiveUi.PressEnterToContinue();
            }
            else if (choice == Loc.T("profiles.add"))
            {
                InteractiveUi.SafeClear();
                InteractiveUi.WriteHeader(Loc.T("profiles.add_subtitle"));
                AddOrEditProfile(config, profileManager, existing: null);
            }
            else if (choice == Loc.T("profiles.edit"))
            {
                EditExisting(config, profileManager);
            }
            else if (choice == Loc.T("profiles.delete"))
            {
                DeleteProfile(config, profileManager);
            }
            else
            {
                return;
            }
        }
    }

    private static void EditExisting(LimboConfig config, ProfileManager profileManager)
    {
        var profiles = profileManager.GetAllProfiles();
        if (profiles.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{Loc.T("profiles.none_to_edit")}[/]");
            InteractiveUi.PressEnterToContinue();
            return;
        }

        var domain = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Loc.T("profiles.select_to_edit"))
                .AddChoices(profiles.Select(p => p.Domain).Append(Loc.T("back"))));

        if (domain == Loc.T("back")) return;

        var existing = profiles.First(p => p.Domain == domain);
        InteractiveUi.SafeClear();
        InteractiveUi.WriteHeader(Loc.T("profiles.edit_subtitle", domain));
        AddOrEditProfile(config, profileManager, existing);
    }

    private static void DeleteProfile(LimboConfig config, ProfileManager profileManager)
    {
        var profiles = profileManager.GetAllProfiles();
        if (profiles.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{Loc.T("profiles.none_to_delete")}[/]");
            InteractiveUi.PressEnterToContinue();
            return;
        }

        var domain = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Loc.T("profiles.select_to_delete"))
                .AddChoices(profiles.Select(p => p.Domain).Append(Loc.T("back"))));

        if (domain == Loc.T("back")) return;

        if (!AnsiConsole.Confirm(Loc.T("profiles.confirm_delete", domain)))
            return;

        profileManager.DeleteProfile(domain);
        config.Profiles.RemoveAll(p => p.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));
        ConfigManager.Save(config);

        AnsiConsole.MarkupLine($"[green]{Loc.T("profiles.deleted")}[/] {domain.EscapeMarkup()}");
        InteractiveUi.PressEnterToContinue();
    }

    private static void AddOrEditProfile(LimboConfig config, ProfileManager profileManager, DomainProfile? existing)
    {
        var domain = AnsiConsole.Prompt(
            new TextPrompt<string>(Loc.T("profiles.domain_prompt"))
                .DefaultValue(existing?.Domain ?? "")
                .ShowDefaultValue(existing != null)
                .Validate(d => string.IsNullOrWhiteSpace(d)
                    ? ValidationResult.Error(Loc.T("profiles.domain_empty_error"))
                    : ValidationResult.Success()));

        var method = AnsiConsole.Prompt(
            new SelectionPrompt<BypassMethod>()
                .Title(Loc.T("profiles.method_prompt"))
                .AddChoices(SelectableMethods));

        var useAdaptive = method == BypassMethod.Adaptive
            || AnsiConsole.Confirm(Loc.T("profiles.use_adaptive_prompt"), existing?.UseAdaptiveParams ?? true);

        var profile = new DomainProfile
        {
            Domain = domain,
            Method = method,
            UseAdaptiveParams = useAdaptive,
            FragmentSize = existing?.FragmentSize ?? 2,
            SplitPosition = existing?.SplitPosition ?? 3,
            SplitOffset = existing?.SplitOffset ?? 3,
            FakeTtl = existing?.FakeTtl ?? 3,
            EchPublicName = existing?.EchPublicName ?? "",
            FakePacketSize = existing?.FakePacketSize ?? 64,
            IsVerified = existing?.IsVerified ?? false
        };

        if (!useAdaptive)
        {
            PromptMethodParameter(method, profile);
        }

        profileManager.SaveProfile(profile);
        SyncToConfig(config, profile);
        ConfigManager.Save(config);

        AnsiConsole.MarkupLine($"\n[green]{Loc.T("profiles.saved")}[/] {domain.EscapeMarkup()} -> {method}");
        InteractiveUi.PressEnterToContinue();
    }

    /// <summary>
    /// Prompts for the single manual parameter relevant to a method and
    /// writes it into the given profile. Shared with RadarMenu's default-
    /// method screen.
    /// </summary>
    internal static void PromptMethodParameter(BypassMethod method, DomainProfile profile)
    {
        switch (method)
        {
            case BypassMethod.TcpFragmentation:
            case BypassMethod.SmartFragmentation:
                profile.FragmentSize = AnsiConsole.Prompt(
                    new TextPrompt<int>(Loc.T("param.fragment_size")).DefaultValue(profile.FragmentSize));
                break;
            case BypassMethod.Desync:
                profile.SplitPosition = AnsiConsole.Prompt(
                    new TextPrompt<int>(Loc.T("param.split_position")).DefaultValue(profile.SplitPosition));
                break;
            case BypassMethod.FakeTtl:
            case BypassMethod.UdpFake:
                profile.FakeTtl = AnsiConsole.Prompt(
                    new TextPrompt<int>(Loc.T("param.fake_ttl")).DefaultValue(profile.FakeTtl));
                break;
            case BypassMethod.Ech:
                profile.EchPublicName = AnsiConsole.Prompt(
                    new TextPrompt<string>(Loc.T("param.ech_name"))
                        .AllowEmpty()
                        .DefaultValue(profile.EchPublicName));
                break;
            case BypassMethod.QuicFake:
                profile.FakePacketSize = AnsiConsole.Prompt(
                    new TextPrompt<int>(Loc.T("param.quic_size")).DefaultValue(profile.FakePacketSize));
                break;
        }
    }

    private static void SyncToConfig(LimboConfig config, DomainProfile profile)
    {
        config.Profiles.RemoveAll(p => p.Domain.Equals(profile.Domain, StringComparison.OrdinalIgnoreCase));
        config.Profiles.Add(new ProfileConfig
        {
            Domain = profile.Domain,
            Method = profile.Method,
            FragmentSize = profile.FragmentSize,
            SplitPosition = profile.SplitPosition,
            SplitOffset = profile.SplitOffset,
            FakeTtl = profile.FakeTtl,
            EchPublicName = profile.EchPublicName,
            FakePacketSize = profile.FakePacketSize,
            UseAdaptiveParams = profile.UseAdaptiveParams
        });
    }
}
