using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Limbo.Core.Profiles;
using Spectre.Console;

namespace Limbo.Console.Commands;

/// <summary>
/// Small helpers shared across the CLI command handlers and the interactive
/// menu screens - argument parsing, number formatting, and syncing the
/// legacy config.json profile list into ProfileManager's per-domain store.
/// </summary>
internal static class CommandHelpers
{
    public static void ReportInterceptionBusy()
    {
        AnsiConsole.MarkupLine("[red]Another Limbo instance is already intercepting traffic[/] (run, calibration or network scan).");
        AnsiConsole.MarkupLine("  Stop it first (Ctrl+C in its window) - two interceptors would both rewrite the same packets.");
    }

    public static void WarnIfZeroDivertRunning()
    {
        if (!InstanceLock.IsZeroDivertRunning()) return;
        AnsiConsole.MarkupLine("[yellow]ZeroDivert is also running.[/] Both apps rewrite Discord's TLS handshakes; running them together can break Discord connections or skew calibration results. Consider stopping one of them.");
    }

    public static void LoadProfilesFromConfig(ProfileManager manager, LimboConfig config)
    {
        foreach (var profileConfig in config.Profiles)
        {
            manager.SaveProfile(new DomainProfile
            {
                Domain = profileConfig.Domain,
                Method = profileConfig.Method,
                FragmentSize = profileConfig.FragmentSize,
                SplitPosition = profileConfig.SplitPosition,
                SplitOffset = profileConfig.SplitOffset,
                FakeTtl = profileConfig.FakeTtl,
                EchPublicName = profileConfig.EchPublicName,
                FakePacketSize = profileConfig.FakePacketSize,
                UseAdaptiveParams = profileConfig.UseAdaptiveParams
            });
        }
    }

    public static int GetIntArg(List<string> args, string flag, int defaultValue)
    {
        var idx = args.IndexOf(flag);
        if (idx >= 0 && idx + 1 < args.Count && int.TryParse(args[idx + 1], out var val))
        {
            args.RemoveAt(idx + 1);
            args.RemoveAt(idx);
            return val;
        }
        return defaultValue;
    }

    public static string GetStringArg(List<string> args, string flag, string defaultValue)
    {
        var idx = args.IndexOf(flag);
        if (idx >= 0 && idx + 1 < args.Count)
        {
            var val = args[idx + 1];
            args.RemoveAt(idx + 1);
            args.RemoveAt(idx);
            return val;
        }
        return defaultValue;
    }

    public static string FormatNumber(long num) => num switch
    {
        >= 1_000_000 => $"{num / 1_000_000.0:F1}M",
        >= 1_000 => $"{num / 1_000.0:F1}K",
        _ => num.ToString()
    };

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F1} KB",
        _ => $"{bytes} B"
    };

    /// <summary>Method-specific parameter summary, e.g. "frag=2" or "ech-name=foo.com".</summary>
    public static string ParamSummary(DomainProfile profile) => profile.Method switch
    {
        BypassMethod.TcpFragmentation or BypassMethod.SmartFragmentation => $"frag={profile.FragmentSize}",
        BypassMethod.FakeTtl or BypassMethod.UdpFake => $"ttl={profile.FakeTtl}",
        BypassMethod.Desync => $"split={profile.SplitPosition}",
        BypassMethod.Ech when !string.IsNullOrEmpty(profile.EchPublicName) => $"ech-name={profile.EchPublicName}",
        BypassMethod.QuicFake => $"size={profile.FakePacketSize}",
        _ => profile.UseAdaptiveParams ? "adaptive" : "-"
    };
}
