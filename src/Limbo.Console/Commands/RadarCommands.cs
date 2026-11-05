using Limbo.Core.Analysis;
using Spectre.Console;

namespace Limbo.Console.Commands;

/// <summary>
/// radar / radar-import / radar-export / radar-clear CLI commands.
/// </summary>
internal static class RadarCommands
{
    public static int Show()
    {
        var blacklistManager = new BlacklistManager();
        var domains = blacklistManager.GetAllBlacklisted();

        if (domains.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Radar: No auto-detected blocked domains yet.[/]");
            AnsiConsole.MarkupLine("[grey]Run the engine and Limbo will auto-detect blocked domains.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[bold]Radar: {domains.Count} auto-detected blocked domain(s)[/]\n");
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Domain");
        table.AddColumn("Method");
        foreach (var domain in domains)
        {
            table.AddRow($"[yellow]{domain.EscapeMarkup()}[/]", "Adaptive");
        }
        AnsiConsole.Write(table);

        return 0;
    }

    public static int Import(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Usage:[/] limbo radar-import <file>");
            return 1;
        }

        return Import(args[0]);
    }

    public static int Import(string filePath)
    {
        if (!File.Exists(filePath))
        {
            AnsiConsole.MarkupLine($"[red]File not found:[/] {filePath.EscapeMarkup()}");
            return 1;
        }

        try
        {
            var blacklistManager = new BlacklistManager();
            blacklistManager.Import(filePath);
            var domains = blacklistManager.GetAllBlacklisted();

            AnsiConsole.MarkupLine($"[green]Radar: Imported {domains.Count} domain(s) from[/] {Path.GetFileName(filePath).EscapeMarkup()}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Import failed:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        return 0;
    }

    public static int Export(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Usage:[/] limbo radar-export <file>");
            return 1;
        }

        return Export(args[0]);
    }

    public static int Export(string filePath)
    {
        try
        {
            var blacklistManager = new BlacklistManager();
            var domains = blacklistManager.GetAllBlacklisted();

            if (domains.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Radar: No domains to export.[/]");
                return 0;
            }

            blacklistManager.Export(filePath);
            AnsiConsole.MarkupLine($"[green]Radar: Exported {domains.Count} domain(s) to[/] {Path.GetFileName(filePath).EscapeMarkup()}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Export failed:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        return 0;
    }

    public static int Clear(bool skipConfirmation = false)
    {
        if (!skipConfirmation)
        {
            if (!AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Refusing to clear without confirmation in a non-interactive session.[/] Re-run with --yes.");
                return 1;
            }

            if (!AnsiConsole.Confirm("Clear all radar-detected domains? This cannot be undone."))
            {
                AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
                return 0;
            }
        }

        try
        {
            var autoBlacklistPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Limbo", "Auto-Blacklist.txt");

            if (File.Exists(autoBlacklistPath))
                File.Delete(autoBlacklistPath);

            AnsiConsole.MarkupLine("[green]Radar blacklist cleared.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to clear:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        return 0;
    }
}
