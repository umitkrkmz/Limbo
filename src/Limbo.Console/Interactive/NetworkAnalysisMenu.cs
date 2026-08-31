using Limbo.Core.Analysis;
using Spectre.Console;

namespace Limbo.Console.Interactive;

/// <summary>
/// Shows the user's public IP/ISP identity (via IspIdentifier) plus local
/// network info, then optionally runs a live NetworkAnalyzer scan against a
/// domain (Administrator-only) reporting DNS tampering, RST injection, or
/// silent dropping.
/// </summary>
internal static class NetworkAnalysisMenu
{
    public static async Task ShowAsync()
    {
        InteractiveUi.SafeClear();
        InteractiveUi.WriteHeader(Loc.T("network.subtitle"));

        PublicNetworkInfo? netInfo = null;
        await AnsiConsole.Status().StartAsync(Loc.T("network.fetching"), async _ =>
        {
            using var identifier = new IspIdentifier();
            netInfo = await identifier.GetPublicNetworkInfoAsync();
        });

        var infoTable = new Table().Border(TableBorder.Rounded).HideHeaders();
        infoTable.AddColumn("Key");
        infoTable.AddColumn("Value");

        if (netInfo != null)
        {
            infoTable.AddRow(Loc.T("network.public_ip"), netInfo.IpAddress.EscapeMarkup());
            infoTable.AddRow(Loc.T("network.isp"), netInfo.Organization.EscapeMarkup());
            var location = string.Join(", ", new[] { netInfo.City, netInfo.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
            infoTable.AddRow(Loc.T("network.location"), location.EscapeMarkup());
        }
        else
        {
            infoTable.AddRow(Loc.T("network.public_ip"), $"[grey]{Loc.T("network.not_available")}[/]");
        }

        var localDns = SystemStatus.GetPrimaryInterfaceDns();
        infoTable.AddRow(Loc.T("network.local_interface"), (localDns?.Name ?? Loc.T("status.not_found")).EscapeMarkup());
        infoTable.AddRow(
            Loc.T("network.local_dns"),
            localDns.HasValue && localDns.Value.DnsServers.Count > 0
                ? string.Join(", ", localDns.Value.DnsServers).EscapeMarkup()
                : Loc.T("status.not_found"));

        AnsiConsole.Write(infoTable);
        AnsiConsole.WriteLine();

        var domain = AnsiConsole.Prompt(
            new TextPrompt<string>(Loc.T("network.domain_prompt")).AllowEmpty());

        if (string.IsNullOrWhiteSpace(domain))
        {
            InteractiveUi.PressEnterToContinue();
            return;
        }

        if (!SystemStatus.IsAdministrator())
        {
            AnsiConsole.MarkupLine($"[yellow]{Loc.T("network.requires_admin_note")}[/]");
        }

        DomainAnalysisResult? result = null;
        var logMessages = new List<string>();
        await AnsiConsole.Status().StartAsync(Loc.T("network.scanning"), async _ =>
        {
            var analyzer = new NetworkAnalyzer();
            analyzer.OnLog += msg => logMessages.Add(msg);
            result = await analyzer.AnalyzeDomainAsync(domain);
        });

        // Printed after the status spinner finishes, not during, so it
        // doesn't fight the spinner for the same terminal region.
        foreach (var msg in logMessages)
        {
            AnsiConsole.MarkupLine($"[grey]{msg.EscapeMarkup()}[/]");
        }

        if (result != null)
        {
            AnsiConsole.WriteLine();
            var resultTable = new Table().Border(TableBorder.Rounded).HideHeaders();
            resultTable.AddColumn("Key");
            resultTable.AddColumn("Value");
            resultTable.AddRow(
                "DNS",
                result.DnsTamperSuspected
                    ? $"[yellow]{Loc.T("network.dns_tamper_yes")}[/]"
                    : $"[green]{Loc.T("network.dns_tamper_no")}[/]");
            resultTable.AddRow(Loc.T("network.outcome"), FormatOutcome(result.Outcome));
            AnsiConsole.Write(resultTable);
        }

        InteractiveUi.PressEnterToContinue();
    }

    private static string FormatOutcome(NetworkAnalysisOutcome outcome) => outcome switch
    {
        NetworkAnalysisOutcome.Reachable => $"[green]{Loc.T("network.outcome.reachable")}[/]",
        NetworkAnalysisOutcome.RstInjectionDetected => $"[red]{Loc.T("network.outcome.rst")}[/]",
        NetworkAnalysisOutcome.SilentlyDropped => $"[red]{Loc.T("network.outcome.silentdrop")}[/]",
        NetworkAnalysisOutcome.Unreachable => $"[yellow]{Loc.T("network.outcome.unreachable")}[/]",
        NetworkAnalysisOutcome.CouldNotResolveDns => $"[yellow]{Loc.T("network.outcome.dnsfail")}[/]",
        NetworkAnalysisOutcome.RequiresAdministrator => $"[grey]{Loc.T("network.outcome.noadmin")}[/]",
        _ => outcome.ToString()
    };
}
