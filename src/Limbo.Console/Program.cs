using Limbo.Core.Config;
using Limbo.Core.Logging;
using Limbo.Core.Profiles;
using Limbo.Console.Commands;
using Limbo.Console.Interactive;
using Spectre.Console;

namespace Limbo.Console;

class Program
{
    private static readonly string Help = """

        Usage: limbo [command] [options]

        Running `limbo` with no command opens the interactive menu.

        ═══════════════════════════════════════════════════════════════════════════
         COMMANDS
        ═══════════════════════════════════════════════════════════════════════════

          run [--global]          Start DPI bypass engine
                                  Monitors network traffic and applies bypass
                                  strategies to configured domains.
                                  --global: GoodbyeDPI-style - also bypass every
                                  domain without a profile, using the default
                                  method (for this run; see globalMode in config).
                                  Requires Administrator privileges.

          calibrate <domain> [port] [--save]
                                  Find best bypass method for a domain
                                  Tests all strategies and measures latency.
                                  Asks whether to save the result as a profile;
                                  --save stores it without asking (for scripts).
                                  Example: limbo calibrate discord.com --save

          test <domain>           Quick connection test
                                  Tests DNS, DoH, and TCP connectivity.
                                  Example: limbo test twitter.com

          add-profile <domain> <method> [options]
                                  Add a new domain profile
                                  Methods: TcpFragmentation, SmartFragmentation,
                                           Desync, FakeTtl, UdpFake, Ech, QuicFake
                                  Options: --frag <size>  Fragment size (default: 2)
                                           --ttl <value>  Fake TTL value (default: 3)
                                           --split <pos>  Split position (default: 3)
                                           --offset <n>   SNI split offset (default: 3)
                                           --ech-name <n> ECH public name
                                           --quic-size <n> QUIC fake packet size (default: 64)
                                  Example: limbo add-profile *.discord.com TcpFragmentation --frag 2

          remove-profile <domain> Remove a domain profile
                                  Example: limbo remove-profile *.twitter.com

          radar                   Show radar system status (auto-detected blocked domains)
                                  Displays all auto-blacklisted domains.

          radar-import <file>     Import a radar blacklist from a text file
                                  Example: limbo radar-import blocked_domains.txt

          radar-export <file>     Export radar blacklist to a text file
                                  Example: limbo radar-export my_blacklist.txt

          radar-clear [--yes]     Clear all auto-detected (radar) entries
                                  Asks for confirmation; --yes (-y) skips it
                                  (required in non-interactive sessions).


        ═══════════════════════════════════════════════════════════════════════════
         OPTIONS
        ═══════════════════════════════════════════════════════════════════════════

          -v, --verbose           Enable verbose logging (show packet details)
          -c, --config <path>     Use custom config file
          --list-profiles         List all configured domain profiles
          --list-methods          List all available bypass methods
          --show-config           Show current configuration
          --version               Show version information
          --help, -h              Show this help message

        ═══════════════════════════════════════════════════════════════════════════
         EXAMPLES
        ═══════════════════════════════════════════════════════════════════════════

          Open the interactive menu:
            limbo

          Start bypass engine directly:
            limbo run -v

          Find best method for Discord:
            limbo calibrate discord.com

          Add profile and start:
            limbo add-profile "*.discord.com" TcpFragmentation --frag 2
            limbo run

          View radar-detected blocked domains:
            limbo radar

          Import a pre-made blocklist:
            limbo radar-import blocked.txt


        ═══════════════════════════════════════════════════════════════════════════
         BYPASS METHODS
        ═══════════════════════════════════════════════════════════════════════════

          TcpFragmentation    Split TCP into small segments (most compatible)
          SmartFragmentation  Split at exact SNI offset (minimal fragments)
          Desync              Send packets out of order (state-based DPI)
          FakeTtl             Decoy packets with low TTL (passive DPI)
          UdpFake             Fake packets for UDP/QUIC traffic
          Ech                 Encrypted Client Hello (Cloudflare sites)
          QuicFake            Fake datagram injection for HTTP/3
          Adaptive            Auto-detect best method (slower startup)

        """;

    static async Task<int> Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        InstanceLock.MarkRunning();

        var argList = args.ToList();
        var verbose = argList.Remove("-v") || argList.Remove("--verbose");
        var showHelp = argList.Remove("--help") || argList.Remove("-h");
        var listProfiles = argList.Remove("--list-profiles");
        var listMethods = argList.Remove("--list-methods");
        var showConfig = argList.Remove("--show-config");
        var showVersion = argList.Remove("--version");

        string? configPath = null;
        var configIdx = argList.IndexOf("-c");
        if (configIdx < 0) configIdx = argList.IndexOf("--config");
        if (configIdx >= 0 && configIdx + 1 < argList.Count)
        {
            configPath = argList[configIdx + 1];
            argList.RemoveAt(configIdx + 1);
            argList.RemoveAt(configIdx);
        }

        var isInteractive = argList.Count == 0 && !showHelp && !showVersion && !listMethods && !showConfig && !listProfiles;
        if (!isInteractive)
        {
            InteractiveUi.WriteHeaderNonInteractive();
        }

        if (showHelp)
        {
            AnsiConsole.WriteLine(Help);
            return 0;
        }

        if (showVersion)
        {
            ConfigCommands.ShowVersion();
            return 0;
        }

        if (listMethods)
        {
            ConfigCommands.ListMethods();
            return 0;
        }

        var config = configPath != null
            ? ConfigManager.Load(configPath)
            : ConfigManager.Load();

        if (verbose) config.Verbose = true;

        using var logger = new LimboLogger(config.LogFile, config.Verbose);

        if (showConfig)
        {
            ConfigCommands.ShowConfig(config);
            return 0;
        }

        if (listProfiles)
        {
            var pm = new ProfileManager();
            CommandHelpers.LoadProfilesFromConfig(pm, config);
            ConfigCommands.ListProfiles(pm);
            return 0;
        }

        if (isInteractive)
        {
            return await InteractiveMenu.RunAsync(config, logger);
        }

        var command = argList[0].ToLower();
        var commandArgs = argList.Skip(1).ToArray();

        return command switch
        {
            "run" => await RunCommand.RunAsync(config, logger, commandArgs),
            "calibrate" => await CalibrateCommand.RunAsync(config, logger, commandArgs),
            "test" => await TestCommand.RunAsync(config, commandArgs),
            "add-profile" => ProfileCommands.AddProfile(config, commandArgs),
            "remove-profile" => ProfileCommands.RemoveProfile(config, commandArgs),
            "radar" => RadarCommands.Show(),
            "radar-import" => RadarCommands.Import(commandArgs),
            "radar-export" => RadarCommands.Export(commandArgs),
            "radar-clear" => RadarCommands.Clear(skipConfirmation: commandArgs.Contains("--yes") || commandArgs.Contains("-y")),
            _ => await RunCommand.RunAsync(config, logger)
        };
    }
}
