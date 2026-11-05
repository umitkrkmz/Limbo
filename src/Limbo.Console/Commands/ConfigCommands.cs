using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Limbo.Core.Profiles;
using Spectre.Console;

namespace Limbo.Console.Commands;

/// <summary>
/// Read-only informational commands: --show-config, --list-profiles,
/// --list-methods, --version.
/// </summary>
internal static class ConfigCommands
{
    public static void ShowConfig(LimboConfig config)
    {
        AnsiConsole.MarkupLine($"[grey]Config file:[/] {ConfigManager.GetPath(config).EscapeMarkup()}");
        AnsiConsole.WriteLine();

        var settings = new Table().Border(TableBorder.Rounded).HideHeaders();
        settings.AddColumn("Key");
        settings.AddColumn("Value");
        settings.AddRow("Filter", config.Filter.EscapeMarkup());
        settings.AddRow("Verbose", FormatBool(config.Verbose));
        settings.AddRow("Adaptive Engine (Radar)", FormatBool(config.EnableRadar));
        settings.AddRow("Radar Default Method", config.DefaultMethodProfile.Method.ToString());
        settings.AddRow("Language", config.Language.ToString());
        settings.AddRow("Update check (interactive menu)", FormatBool(config.CheckForUpdates));
        settings.AddRow("DNS over HTTPS", FormatBool(config.Dns.UseDoH));
        if (config.Dns.UseDoH)
            settings.AddRow("DoH Server", config.Dns.DoHServer.EscapeMarkup());
        AnsiConsole.Write(settings);

        AnsiConsole.MarkupLine($"\n[bold]Profiles ({config.Profiles.Count})[/]");
        if (config.Profiles.Count == 0)
        {
            AnsiConsole.MarkupLine("  [grey](none configured)[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Domain");
        table.AddColumn("Method");
        table.AddColumn("Parameters");
        foreach (var p in config.Profiles)
        {
            table.AddRow(
                $"[cyan]{p.Domain.EscapeMarkup()}[/]",
                p.Method.ToString(),
                ParamSummaryForConfig(p).EscapeMarkup());
        }
        AnsiConsole.Write(table);
    }

    public static void ListProfiles(ProfileManager manager)
    {
        var profiles = manager.GetAllProfiles();
        AnsiConsole.MarkupLine($"[bold]{profiles.Count} profile(s) loaded[/]\n");

        if (profiles.Count == 0)
        {
            AnsiConsole.MarkupLine("  [grey](none)[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Domain");
        table.AddColumn("Method");
        table.AddColumn("Parameters");
        table.AddColumn("Verified");
        table.AddColumn("Adaptive");

        foreach (var profile in profiles)
        {
            table.AddRow(
                $"[cyan]{profile.Domain.EscapeMarkup()}[/]",
                profile.Method.ToString(),
                CommandHelpers.ParamSummary(profile).EscapeMarkup(),
                profile.IsVerified ? "[green]yes[/]" : "[grey]no[/]",
                profile.UseAdaptiveParams ? "[green]yes[/]" : "[grey]no[/]");
        }
        AnsiConsole.Write(table);
    }

    public static void ListMethods()
    {
        AnsiConsole.MarkupLine("[bold]Available Bypass Methods[/]\n");

        var methods = new (string Name, int Priority, string Description)[]
        {
            ("TcpFragmentation",   100, "Split TCP into small segments (most compatible)"),
            ("SmartFragmentation",  90, "Split at exact SNI offset (minimal fragments)"),
            ("Desync",              80, "Send packets out of order (state-based DPI)"),
            ("FakeTtl",             70, "Decoy packets with low TTL (passive DPI)"),
            ("UdpFake",             60, "Fake packets for UDP/QUIC traffic"),
            ("QuicFake",           150, "Fake datagram injection for HTTP/3"),
            ("Ech",                200, "Encrypted Client Hello (Cloudflare sites)"),
            ("Adaptive",             0, "Auto-detect best method (slower startup)"),
            ("None",                 0, "Pass through without modification")
        };

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn(new TableColumn("Priority").RightAligned());
        table.AddColumn("Description");

        foreach (var (name, priority, desc) in methods)
        {
            table.AddRow(
                $"[cyan]{name}[/]",
                priority > 0 ? priority.ToString() : "[grey]-[/]",
                desc.EscapeMarkup());
        }
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("\n[grey]" + "Usage: limbo add-profile <domain> <method> [--frag <n>] [--ttl <n>]".EscapeMarkup() + "[/]");
    }

    public static void ShowVersion()
    {
        var panel = new Panel(
                $$"""
                [bold]Limbo DPI Bypass Engine[/]
                Version: {{AppInfo.VersionText}}
                License: {{AppInfo.License}}

                [bold]Components[/]
                  Limbo.Core       Bypass strategies, detection, radar
                  Limbo.Driver     WinDivert integration
                  Limbo.Console    Interactive + scriptable CLI

                [bold]Strategies[/]
                  TCP: Fragmentation (pri:100), SmartFrag (pri:90), Desync (pri:80), FakeTTL (pri:70)
                  UDP: UdpFake (pri:60), QuicFake (pri:150)
                  TLS: ECH - Encrypted Client Hello (pri:200)

                [bold]Features[/]
                  Radar System (auto-detection of blocked domains)
                  ISP Behavior Analysis (RST injection, silent drops)
                  Network Analysis (ISP lookup + live DPI behavior scan)
                  DNS over HTTPS (Cloudflare, Google, Quad9)
                  Bandwidth Monitoring
                  Per-domain profiles with wildcard support
                  Profile calibration

                [grey]GitHub: {{AppInfo.RepoUrl}}[/]
                """)
            .Border(BoxBorder.Rounded)
            .Padding(1, 0);

        AnsiConsole.Write(panel);
    }

    private static string FormatBool(bool value) => value ? "[green]yes[/]" : "[grey]no[/]";

    private static string ParamSummaryForConfig(ProfileConfig p) => p.Method switch
    {
        BypassMethod.TcpFragmentation or BypassMethod.SmartFragmentation => $"frag={p.FragmentSize}",
        BypassMethod.FakeTtl or BypassMethod.UdpFake => $"ttl={p.FakeTtl}",
        BypassMethod.Desync => $"split={p.SplitPosition}",
        BypassMethod.Ech when !string.IsNullOrEmpty(p.EchPublicName) => $"ech-name={p.EchPublicName}",
        BypassMethod.QuicFake => $"size={p.FakePacketSize}",
        _ => p.UseAdaptiveParams ? "adaptive" : "-"
    };
}
