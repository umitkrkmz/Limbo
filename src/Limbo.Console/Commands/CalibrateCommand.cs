using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Limbo.Core.Engine;
using Limbo.Core.Logging;
using Limbo.Core.Profiles;
using Limbo.Console.Interactive;
using Spectre.Console;

namespace Limbo.Console.Commands;

/// <summary>
/// calibrate CLI command - finds the best bypass method for a domain.
/// The core calibration logic is also reused by the interactive menu.
/// </summary>
internal static class CalibrateCommand
{
    public static async Task<int> RunAsync(LimboConfig config, LimboLogger logger, string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Usage:[/] limbo calibrate <domain> [port]");
            return 1;
        }

        var domain = args[0];
        var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 443;

        var result = await CalibrateAsync(config, logger, domain, port);

        if (result.Success && AnsiConsole.Confirm($"Add profile for *.{domain}?"))
        {
            SaveAsProfile(config, domain, result);
        }

        return 0;
    }

    /// <summary>
    /// Runs calibration with a live progress bar and prints the outcome.
    /// Does not prompt to save - callers decide whether/how to offer that.
    /// </summary>
    public static async Task<CalibrationResult> CalibrateAsync(LimboConfig config, LimboLogger logger, string domain, int port = 443)
    {
        AnsiConsole.MarkupLine($"[bold]Calibrating bypass for:[/] {domain.EscapeMarkup()}:{port}");

        using var interception = InstanceLock.TryAcquireInterception();
        if (interception == null)
        {
            CommandHelpers.ReportInterceptionBusy();
            return new CalibrationResult
            {
                Domain = domain,
                Success = false,
                BestMethod = BypassMethod.None,
                LatencyMs = 0,
                Message = "Another Limbo instance is intercepting traffic"
            };
        }

        CommandHelpers.WarnIfZeroDivertRunning();

        if (!SystemStatus.IsAdministrator())
        {
            AnsiConsole.MarkupLine("[yellow]Not running as Administrator - strategies can't actually be applied during this test, so every strategy will only reflect plain connectivity, not real bypass effectiveness.[/]");
        }

        var profileManager = new ProfileManager();
        var bypassEngine = BypassEngine.CreateDefault();
        var calibrator = new CalibrationEngine(profileManager, bypassEngine);

        calibrator.OnLog += msg => logger.Info(msg);

        CalibrationResult result = null!;
        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Testing strategies");
                calibrator.OnProgress += progress =>
                {
                    task.Description = $"Testing {progress.CurrentMethod}";
                    task.Value = progress.TotalStrategies > 0
                        ? (double)progress.Progress / progress.TotalStrategies * 100
                        : 0;
                };

                result = await calibrator.CalibrateAsync(domain, port);
                task.Value = 100;
            });

        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]Best method:[/] {result.BestMethod}");
            AnsiConsole.MarkupLine($"[green]Latency:[/]     {result.LatencyMs}ms");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]{result.Message.EscapeMarkup()}[/]");
        }

        return result;
    }

    public static void SaveAsProfile(LimboConfig config, string domain, CalibrationResult result)
    {
        config.Profiles.Add(new ProfileConfig
        {
            Domain = $"*.{domain}",
            Method = result.BestMethod
        });
        ConfigManager.Save(config);
        AnsiConsole.MarkupLine("[green]Profile added and saved to config[/]");
    }
}
