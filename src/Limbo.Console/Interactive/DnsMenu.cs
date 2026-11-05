using Limbo.Core.Config;
using Limbo.Console.Commands;
using Spectre.Console;

namespace Limbo.Console.Interactive;

/// <summary>
/// DNS settings: DoH toggle, provider picker, system DNS display, live test.
/// </summary>
internal static class DnsMenu
{
    private static (string Label, string Url)[] Providers =>
    [
        ("Cloudflare (1.1.1.1)", "https://cloudflare-dns.com/dns-query"),
        ("Google (8.8.8.8)", "https://dns.google/dns-query"),
        ("Quad9 (9.9.9.9)", "https://dns.quad9.net/dns-query"),
        (Loc.T("dns.custom_option"), "")
    ];

    public static void Show(LimboConfig config)
    {
        while (true)
        {
            InteractiveUi.SafeClear();
            InteractiveUi.WriteHeader(Loc.T("dns.subtitle"));

            var systemDns = SystemStatus.GetPrimaryInterfaceDns();
            var systemDnsText = systemDns.HasValue && systemDns.Value.DnsServers.Count > 0
                ? string.Join(", ", systemDns.Value.DnsServers)
                : Loc.T("dns.not_found");
            var interfaceText = systemDns?.Name ?? Loc.T("dns.not_found");

            var table = new Table().Border(TableBorder.Rounded).HideHeaders();
            table.AddColumn("Key");
            table.AddColumn("Value");
            table.AddRow(Loc.T("dns.interface"), interfaceText.EscapeMarkup());
            table.AddRow(Loc.T("dns.system_dns"), systemDnsText.EscapeMarkup());
            table.AddRow(Loc.T("dns.doh"), config.Dns.UseDoH ? $"[green]{Loc.T("on")}[/]" : $"[grey]{Loc.T("off")}[/]");
            if (config.Dns.UseDoH)
                table.AddRow(Loc.T("dns.server"), config.Dns.DoHServer.EscapeMarkup());
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]{Loc.T("dns.doh_note")}[/]");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Loc.T("dns.prompt"))
                    .AddChoices(Loc.T("dns.toggle"), Loc.T("dns.pick_provider"), Loc.T("dns.test_now"), Loc.T("back")));

            if (choice == Loc.T("dns.toggle"))
            {
                config.Dns.UseDoH = !config.Dns.UseDoH;
                ConfigManager.Save(config);
                AnsiConsole.MarkupLine(config.Dns.UseDoH ? $"[green]{Loc.T("dns.enabled")}[/]" : $"[grey]{Loc.T("dns.disabled")}[/]");
                InteractiveUi.PressEnterToContinue();
            }
            else if (choice == Loc.T("dns.pick_provider"))
            {
                PickProvider(config);
            }
            else if (choice == Loc.T("dns.test_now"))
            {
                TestNow(config);
            }
            else
            {
                return;
            }
        }
    }

    private static void PickProvider(LimboConfig config)
    {
        var providers = Providers;
        var label = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Loc.T("dns.provider_prompt"))
                .AddChoices(providers.Select(p => p.Label)));

        var provider = providers.First(p => p.Label == label);
        var url = provider.Url;
        if (string.IsNullOrEmpty(url))
        {
            url = AnsiConsole.Ask(Loc.T("dns.custom_url_prompt"), config.Dns.DoHServer);
        }

        config.Dns.DoHServer = url;
        config.Dns.UseDoH = true;
        ConfigManager.Save(config);
        AnsiConsole.MarkupLine($"[green]{Loc.T("dns.saved")}[/] {url.EscapeMarkup()}");
        InteractiveUi.PressEnterToContinue();
    }

    private static void TestNow(LimboConfig config)
    {
        var domain = AnsiConsole.Ask(Loc.T("dns.test_domain_prompt"), "discord.com");
        AnsiConsole.WriteLine();
        TestCommand.RunAsync(config, domain).GetAwaiter().GetResult();
        InteractiveUi.PressEnterToContinue();
    }
}
