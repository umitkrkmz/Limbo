using Limbo.Core.Config;
using Limbo.Core.Dns;
using Spectre.Console;

namespace Limbo.Console.Commands;

/// <summary>
/// test CLI command - quick DNS/DoH/TCP connectivity check for a domain.
/// </summary>
internal static class TestCommand
{
    public static async Task<int> RunAsync(LimboConfig config, string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Usage:[/] limbo test <domain>");
            return 1;
        }

        await RunAsync(config, args[0]);
        return 0;
    }

    public static async Task RunAsync(LimboConfig config, string domain)
    {
        AnsiConsole.MarkupLine($"[bold]Testing connection to:[/] {domain.EscapeMarkup()}\n");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Check");
        table.AddColumn("Result");

        await AnsiConsole.Status().StartAsync("Running checks...", async ctx =>
        {
            table.AddRow("System DNS", await CheckSystemDns(domain));

            if (config.Dns.UseDoH)
            {
                var (dohResult, tamperResult) = await CheckDoH(config, domain);
                table.AddRow($"DoH ({config.Dns.DoHServer.Replace("https://", "").EscapeMarkup()})", dohResult);
                table.AddRow("DNS Tamper Check", tamperResult);
            }

            table.AddRow("TCP:443", await CheckTcp(domain, 443));
            table.AddRow("TCP:80", await CheckTcp(domain, 80));
        });

        AnsiConsole.Write(table);
    }

    private static async Task<string> CheckSystemDns(string domain)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var addresses = await System.Net.Dns.GetHostAddressesAsync(domain);
            sw.Stop();
            var addrList = string.Join(", ", addresses.Take(3).Select(a => a.ToString()));
            return $"[green]OK[/] ({addresses.Length} addr, {sw.ElapsedMilliseconds}ms) {addrList.EscapeMarkup()}";
        }
        catch (Exception ex)
        {
            return $"[red]FAIL[/] ({ex.Message.EscapeMarkup()})";
        }
    }

    private static async Task<(string DohResult, string TamperResult)> CheckDoH(LimboConfig config, string domain)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doh = new DoHResolver(config.Dns.DoHServer);
            var addresses = await doh.ResolveAsync(domain);
            sw.Stop();
            var addrList = string.Join(", ", addresses.Take(3));
            var dohResult = $"[green]OK[/] ({addresses.Length} addr, {sw.ElapsedMilliseconds}ms) {addrList.EscapeMarkup()}";

            var isBlocked = await doh.IsBlockedAsync(domain);
            var tamperResult = isBlocked
                ? "[yellow]MISMATCH[/] (ISP DNS may be tampered)"
                : "[green]OK[/] (DNS matches DoH)";

            return (dohResult, tamperResult);
        }
        catch (Exception ex)
        {
            return ($"[red]FAIL[/] ({ex.Message.EscapeMarkup()})", "[grey]skipped[/]");
        }
    }

    private static async Task<string> CheckTcp(string domain, int port)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(domain, port);
            sw.Stop();
            return $"[green]OK[/] ({sw.ElapsedMilliseconds}ms)";
        }
        catch (Exception ex)
        {
            return $"[red]FAIL[/] ({ex.Message.EscapeMarkup()})";
        }
    }
}
