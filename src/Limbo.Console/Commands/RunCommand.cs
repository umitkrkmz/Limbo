using Limbo.Core.Config;
using Limbo.Core.Engine;
using Limbo.Core.Logging;
using Limbo.Core.Monitoring;
using Limbo.Core.Profiles;
using Limbo.Console.Interactive;
using Limbo.Driver;
using Spectre.Console;

namespace Limbo.Console.Commands;

/// <summary>
/// run CLI command - starts the DPI bypass engine (LimboEngine) and shows a
/// live-updating stats dashboard while it captures traffic. The packet
/// processing loop itself lives in LimboEngine; this is just the WinDivert
/// handle setup, Ctrl+C handling, and Spectre Live rendering around it.
/// </summary>
internal static class RunCommand
{
    public static async Task<int> RunAsync(LimboConfig config, LimboLogger logger)
    {
        if (!SystemStatus.IsAdministrator())
        {
            AnsiConsole.MarkupLine("[red]Administrator privileges required[/]");
            AnsiConsole.MarkupLine("  Right-click and select 'Run as administrator'");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Running with Administrator privileges[/]");

        using var interception = InstanceLock.TryAcquireInterception();
        if (interception == null)
        {
            CommandHelpers.ReportInterceptionBusy();
            return 1;
        }

        CommandHelpers.WarnIfZeroDivertRunning();

        var profileManager = new ProfileManager();
        CommandHelpers.LoadProfilesFromConfig(profileManager, config);

        using var engine = new LimboEngine(
            profileManager,
            enableRadar: config.EnableRadar,
            defaultMethodProfile: config.DefaultMethodProfile);

        engine.OnLog += msg => logger.Info(msg);
        engine.ConfigureDns(config.Dns);

        if (!config.Dns.UseDoH)
        {
            AnsiConsole.MarkupLine("[grey]DNS over HTTPS is off: other apps' DNS queries go out as-is. If your ISP poisons DNS (e.g. discord.com -> a block page), turn it on in DNS Settings.[/]");
        }

        if (!config.EnableRadar)
        {
            logger.Info("[RADAR] Adaptive engine disabled - auto-detection off (manual profiles only)");
        }

        logger.Info($"Loaded {profileManager.GetAllProfiles().Count} domain profile(s)");

        AnsiConsole.MarkupLine("\n[bold]Initializing WinDivert...[/]");

        using var driver = new WinDivertDriver();
        if (!driver.Open(config.Filter))
        {
            AnsiConsole.MarkupLine("[red]Failed to open WinDivert handle[/]");
            AnsiConsole.MarkupLine("  Make sure WinDivert.dll and WinDivert64.sys are present");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]WinDivert initialized successfully![/]");
        AnsiConsole.MarkupLine("[grey]Monitoring traffic... Press Ctrl+C to stop.[/]\n");

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            engine.Stop(); // cancels the capture loop and disposes the driver to unblock the pending WinDivertRecv call
            // Safety net: if the native call somehow never unblocks, don't hang forever.
            Task.Delay(3000).ContinueWith(_ =>
            {
                if (!Environment.HasShutdownStarted) Environment.Exit(0);
            });
        };
        System.Console.CancelKeyPress += cancelHandler;

        await engine.StartAsync(driver, config.Filter);
        await RenderLiveDashboard(engine, config.Dns.UseDoH);

        System.Console.CancelKeyPress -= cancelHandler;

        PrintFinalStats(engine, config.Dns.UseDoH);

        return 0;
    }

    private static async Task RenderLiveDashboard(LimboEngine engine, bool dohEnabled)
    {
        await AnsiConsole.Live(BuildStatsTable(engine, dohEnabled))
            .StartAsync(async ctx =>
            {
                while (engine.IsRunning)
                {
                    ctx.UpdateTarget(BuildStatsTable(engine, dohEnabled));
                    ctx.Refresh();
                    await Task.Delay(300);
                }

                ctx.UpdateTarget(BuildStatsTable(engine, dohEnabled));
                ctx.Refresh();
            });
    }

    private static Table BuildStatsTable(LimboEngine engine, bool dohEnabled)
    {
        var stats = engine.GetStats();
        var bw = engine.Bandwidth.GetStats();
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Live Traffic[/]");
        table.AddColumn("Time");
        table.AddColumn("Packets");
        table.AddColumn("Matched");
        table.AddColumn("Modified");
        table.AddColumn("Data");
        table.AddColumn("Download");
        table.AddColumn("Upload");
        table.AddColumn("DNS via DoH");

        table.AddRow(
            DateTime.Now.ToString("HH:mm:ss"),
            CommandHelpers.FormatNumber(stats.PacketsProcessed),
            stats.MatchedPackets.ToString(),
            stats.PacketsModified.ToString(),
            CommandHelpers.FormatBytes(stats.BytesProcessed),
            $"[green]↓ {BandwidthStats.FormatRate(bw.DownloadRate)}[/]",
            $"[cyan]↑ {BandwidthStats.FormatRate(bw.UploadRate)}[/]",
            FormatDns(stats, dohEnabled));

        return table;
    }

    private static string FormatDns(EngineStats stats, bool dohEnabled)
    {
        if (!dohEnabled) return "[grey]off[/]";
        return stats.DnsFellBackToPlain == 0
            ? stats.DnsAnsweredViaDoH.ToString()
            : $"{stats.DnsAnsweredViaDoH} [yellow]({stats.DnsFellBackToPlain} plain)[/]";
    }

    private static void PrintFinalStats(LimboEngine engine, bool dohEnabled)
    {
        var stats = engine.GetStats();
        var bw = engine.Bandwidth.GetStats();

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Final Statistics[/]").HideHeaders();
        table.AddColumn("Key");
        table.AddColumn("Value");
        table.AddRow("Packets Processed", CommandHelpers.FormatNumber(stats.PacketsProcessed));
        table.AddRow("Packets Modified", stats.PacketsModified.ToString());
        table.AddRow("Matched Traffic", stats.MatchedPackets.ToString());
        table.AddRow("Data Processed", CommandHelpers.FormatBytes(stats.BytesProcessed));
        table.AddRow("Peak Download", BandwidthStats.FormatRate(bw.PeakDownload));
        table.AddRow("Peak Upload", BandwidthStats.FormatRate(bw.PeakUpload));
        table.AddRow("DNS answered via DoH", FormatDns(stats, dohEnabled));
        AnsiConsole.Write(table);

        var allBlacklisted = engine.AutoBlacklist.GetAllBlacklisted();
        if (allBlacklisted.Count > 0)
        {
            AnsiConsole.MarkupLine($"\n[yellow]Radar:[/] auto-detected blocked domains this session: {allBlacklisted.Count}");
        }
    }
}
