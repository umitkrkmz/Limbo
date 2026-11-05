using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Spectre.Console;

namespace Limbo.Console.Commands;

/// <summary>
/// add-profile / remove-profile CLI commands.
/// </summary>
internal static class ProfileCommands
{
    public static int AddProfile(LimboConfig config, string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine("[yellow]Usage:[/] limbo add-profile <domain> <method> [options]");
            AnsiConsole.MarkupLine("  Methods: TcpFragmentation, SmartFragmentation, Desync, FakeTtl, UdpFake, Ech, QuicFake");
            AnsiConsole.MarkupLine("  Options: --frag <n>  --ttl <n>  --split <n>  --offset <n>  --ech-name <s>  --quic-size <n>");
            return 1;
        }

        var domain = args[0];
        if (!Enum.TryParse<BypassMethod>(args[1], true, out var method))
        {
            AnsiConsole.MarkupLine($"[red]Unknown method:[/] {args[1].EscapeMarkup()}");
            AnsiConsole.MarkupLine("  Use --list-methods to see available methods");
            return 1;
        }

        var argList = args.Skip(2).ToList();
        var fragmentSize = CommandHelpers.GetIntArg(argList, "--frag", 2);
        var fakeTtl = CommandHelpers.GetIntArg(argList, "--ttl", 3);
        var splitPosition = CommandHelpers.GetIntArg(argList, "--split", 3);
        var splitOffset = CommandHelpers.GetIntArg(argList, "--offset", 3);
        var quicSize = CommandHelpers.GetIntArg(argList, "--quic-size", 64);
        var echName = CommandHelpers.GetStringArg(argList, "--ech-name", "");

        config.Profiles.Add(new ProfileConfig
        {
            Domain = domain,
            Method = method,
            FragmentSize = fragmentSize,
            FakeTtl = fakeTtl,
            SplitPosition = splitPosition,
            SplitOffset = splitOffset,
            FakePacketSize = quicSize,
            EchPublicName = echName,
            UseAdaptiveParams = true
        });

        ConfigManager.Save(config);

        AnsiConsole.MarkupLine($"[green]Added profile:[/] {domain.EscapeMarkup()} -> {method}");
        AnsiConsole.MarkupLine($"  FragmentSize:   {fragmentSize}");
        AnsiConsole.MarkupLine($"  FakeTtl:        {fakeTtl}");
        AnsiConsole.MarkupLine($"  SplitPosition:  {splitPosition}");
        AnsiConsole.MarkupLine($"  SplitOffset:    {splitOffset}");
        if (!string.IsNullOrEmpty(echName))
            AnsiConsole.MarkupLine($"  ECH PublicName: {echName.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  Config saved to: {ConfigManager.GetPath(config).EscapeMarkup()}");

        return 0;
    }

    public static int RemoveProfile(LimboConfig config, string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Usage:[/] limbo remove-profile <domain>");
            return 1;
        }

        var domain = args[0];
        var removed = config.Profiles.RemoveAll(p =>
            p.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            ConfigManager.Save(config);
            AnsiConsole.MarkupLine($"[green]Removed {removed} profile(s) matching:[/] {domain.EscapeMarkup()}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]No profile found matching:[/] {domain.EscapeMarkup()}");
        }

        return 0;
    }
}
