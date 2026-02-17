using Limbo.Core.Analysis;
using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Limbo.Core.Detection;
using Limbo.Core.Dns;
using Limbo.Core.Engine;
using Limbo.Core.Logging;
using Limbo.Core.Monitoring;
using Limbo.Core.Profiles;
using Limbo.Driver;

namespace Limbo.Console;

class Program
{
    private static readonly string Logo = """
          _      _           _
         | |    (_)         | |
         | |     _ _ __ ___ | |__   ___
         | |    | | '_ ` _ \| '_ \ / _ \
         | |____| | | | | | | |_) | (_) |
         |______|_|_| |_| |_|_.__/ \___/

              DPI Bypass Engine v1.0
        ──────────────────────────────────
        """;

    private static readonly string Help = """

        Usage: limbo [command] [options]

        ═══════════════════════════════════════════════════════════════════════════
         COMMANDS
        ═══════════════════════════════════════════════════════════════════════════

          run                     Start DPI bypass engine (default)
                                  Monitors network traffic and applies bypass
                                  strategies to configured domains.
                                  Requires Administrator privileges.

          calibrate <domain>      Find best bypass method for a domain
                                  Tests all strategies and measures latency.
                                  Example: limbo calibrate discord.com

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

          radar-clear             Clear all auto-detected (radar) entries


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

          Start bypass engine:
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
        System.Console.WriteLine(Logo);

        // Parse arguments
        var argList = args.ToList();
        var verbose = argList.Remove("-v") || argList.Remove("--verbose");
        var showHelp = argList.Remove("--help") || argList.Remove("-h");
        var listProfiles = argList.Remove("--list-profiles");
        var listMethods = argList.Remove("--list-methods");
        var showConfig = argList.Remove("--show-config");
        var showVersion = argList.Remove("--version");

        // Custom config path
        string? configPath = null;
        var configIdx = argList.IndexOf("-c");
        if (configIdx < 0) configIdx = argList.IndexOf("--config");
        if (configIdx >= 0 && configIdx + 1 < argList.Count)
        {
            configPath = argList[configIdx + 1];
            argList.RemoveAt(configIdx + 1);
            argList.RemoveAt(configIdx);
        }

        if (showHelp)
        {
            System.Console.WriteLine(Help);
            return 0;
        }

        if (showVersion)
        {
            ShowVersion();
            return 0;
        }

        if (listMethods)
        {
            ListMethods();
            return 0;
        }

        // Load configuration
        var config = configPath != null
            ? ConfigManager.Load(configPath)
            : ConfigManager.Load();

        if (verbose) config.Verbose = true;

        // Initialize logger
        using var logger = new LimboLogger(config.LogFile, config.Verbose);

        if (showConfig)
        {
            ShowConfig(config);
            return 0;
        }

        if (listProfiles)
        {
            var pm = new ProfileManager();
            LoadProfilesFromConfig(pm, config);
            ListProfiles(pm);
            return 0;
        }

        // Get command (default: run)
        var command = argList.Count > 0 ? argList[0].ToLower() : "run";
        var commandArgs = argList.Skip(1).ToArray();

        return command switch
        {
            "run" => await RunBypassAsync(config, logger),
            "calibrate" => await CalibrateAsync(config, logger, commandArgs),
            "test" => await TestConnectionAsync(config, commandArgs),
            "add-profile" => AddProfile(config, commandArgs),
            "remove-profile" => RemoveProfile(config, commandArgs),
            "radar" => ShowRadar(),
            "radar-import" => RadarImport(commandArgs),
            "radar-export" => RadarExport(commandArgs),
            "radar-clear" => RadarClear(),
            _ => await RunBypassAsync(config, logger)
        };
    }

    private static async Task<int> RunBypassAsync(LimboConfig config, LimboLogger logger)
    {
        // Check admin privileges
        if (!IsAdministrator())
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("[!] Administrator privileges required");
            System.Console.WriteLine("    Right-click and select 'Run as administrator'");
            System.Console.ResetColor();
            return 1;
        }

        System.Console.ForegroundColor = ConsoleColor.Green;
        System.Console.WriteLine("[+] Running with Administrator privileges");
        System.Console.ResetColor();

        // Initialize profile manager from config
        var profileManager = new ProfileManager();
        LoadProfilesFromConfig(profileManager, config);

        // Initialize Radar system
        var blacklistManager = new BlacklistManager();
        var analyzer = new IspBehaviorAnalyzer();
        var tracker = new ConnectionTracker();
        var bandwidthMonitor = new BandwidthMonitor();

        // Wire up Radar
        analyzer.OnBlockDetected += domain =>
        {
            blacklistManager.AddToBlacklist(domain);
        };

        tracker.OnSilentDropDetected += domain =>
        {
            blacklistManager.AddToBlacklist(domain);
            System.Console.WriteLine($"\n[RADAR] Timeout/Silent Drop Detected: {domain}");
        };

        blacklistManager.OnNewBlacklistedDomain += domain =>
        {
            var newProfile = new DomainProfile
            {
                Domain = domain,
                Method = BypassMethod.Adaptive,
                IsVerified = false
            };
            profileManager.SaveProfile(newProfile);
            System.Console.WriteLine($"\n[RADAR] Auto-Blacklisted: {domain} → Adaptive profile created");
        };

        // Load existing blacklisted domains into profile manager
        var blacklisted = blacklistManager.GetAllBlacklisted();
        foreach (var domain in blacklisted)
        {
            if (profileManager.GetProfile(domain).Method == BypassMethod.None)
            {
                profileManager.SaveProfile(new DomainProfile { Domain = domain, Method = BypassMethod.Adaptive });
            }
        }

        // Show loaded profiles
        var profiles = profileManager.GetAllProfiles();
        logger.Info($"Loaded {profiles.Count} domain profiles:");
        foreach (var profile in profiles.Take(5))
        {
            System.Console.WriteLine($"    - {profile.Domain}: {profile.Method}");
        }
        if (profiles.Count > 5)
        {
            System.Console.WriteLine($"    ... and {profiles.Count - 5} more");
        }

        if (blacklisted.Count > 0)
        {
            System.Console.WriteLine($"    + {blacklisted.Count} radar-detected domain(s) loaded");
        }

        // DNS over HTTPS check
        if (config.Dns.UseDoH)
        {
            logger.Info($"DNS over HTTPS enabled: {config.Dns.DoHServer}");
        }

        // Start capture with WinDivert
        System.Console.WriteLine("\n[*] Initializing WinDivert...");

        using var driver = new WinDivertDriver();

        if (!driver.Open(config.Filter))
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("[!] Failed to open WinDivert handle");
            System.Console.WriteLine("    Make sure WinDivert.dll and WinDivert64.sys are present");
            System.Console.ResetColor();
            return 1;
        }

        System.Console.ForegroundColor = ConsoleColor.Green;
        System.Console.WriteLine("[+] WinDivert initialized successfully!");
        System.Console.ResetColor();
        System.Console.WriteLine("[*] Monitoring traffic... Press Ctrl+C to stop.\n");

        // Statistics
        long packetsProcessed = 0;
        long bytesProcessed = 0;
        long matchedPackets = 0;
        long modifiedPackets = 0;

        // Handle Ctrl+C
        var cts = new CancellationTokenSource();
        System.Console.CancelKeyPress += (_, e) =>
        {
            System.Console.WriteLine("\n[*] Shutting down...");
            e.Cancel = true;
            cts.Cancel();
            driver?.Dispose();
            Task.Delay(500).ContinueWith(_ => Environment.Exit(0));
        };

        // Stats display timer
        var statsTimer = new System.Timers.Timer(1000);
        statsTimer.Elapsed += (_, _) =>
        {
            var bwStats = bandwidthMonitor.GetStats();
            System.Console.Write($"\r[{DateTime.Now:HH:mm:ss}] ");
            System.Console.Write($"Pkts: {FormatNumber(Interlocked.Read(ref packetsProcessed))} | ");
            System.Console.Write($"Matched: {Interlocked.Read(ref matchedPackets)} | ");
            System.Console.Write($"Modified: {Interlocked.Read(ref modifiedPackets)} | ");
            System.Console.Write($"{FormatBytes(Interlocked.Read(ref bytesProcessed))} | ");
            System.Console.Write($"↓{BandwidthStats.FormatRate(bwStats.DownloadRate)} ↑{BandwidthStats.FormatRate(bwStats.UploadRate)}     ");
        };
        statsTimer.Start();

        // Main capture loop
        var buffer = new byte[65535];
        var bypassEngine = new BypassEngine();

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (driver.Receive(buffer, out var length, out var isOutbound))
                {
                    Interlocked.Increment(ref packetsProcessed);
                    Interlocked.Add(ref bytesProcessed, length);
                    bandwidthMonitor.RecordBytes(length, !isOutbound);

                    var packet = buffer.AsSpan(0, length);
                    var context = PacketInspector.Analyze(packet, isOutbound);

                    if (!isOutbound)
                    {
                        // Register inbound to track silent drops
                        var srcIp = GetIpAddress(packet, context.IsIPv6, getSource: true);
                        tracker.RegisterInbound(srcIp, context.SrcPort);

                        driver.Send(packet, false);
                        continue;
                    }

                    // Track outbound for silent drop detection
                    if ((context.IsClientHello || context.HasHttpHost) && !string.IsNullOrEmpty(context.TargetDomain))
                    {
                        var dstIp = GetIpAddress(packet, context.IsIPv6, getSource: false);
                        tracker.TrackOutbound(dstIp, context.DstPort, context.TargetDomain);
                    }

                    if (context.IsClientHello && context.TargetDomain != null)
                    {
                        var profile = profileManager.GetProfile(context.TargetDomain);

                        if (profile.Method != BypassMethod.None)
                        {
                            Interlocked.Increment(ref matchedPackets);

                            if (config.Verbose)
                            {
                                System.Console.WriteLine($"\n[!] Matched: {context.TargetDomain} → {profile.Method}");
                            }

                            var packetContext = new PacketContext
                            {
                                IsOutbound = true,
                                IsTcp = true,
                                IsUdp = false,
                                IsIPv6 = context.IsIPv6,
                                SrcPort = context.SrcPort,
                                DstPort = context.DstPort,
                                IsSyn = context.IsSyn,
                                IsClientHello = true,
                                HasHttpHost = false,
                                TargetDomain = context.TargetDomain
                            };

                            var results = bypassEngine.ProcessPacket(packet, packetContext, profile);

                            if (results.Count > 1)
                            {
                                Interlocked.Increment(ref modifiedPackets);

                                if (config.Verbose)
                                {
                                    System.Console.WriteLine($"\n[+] Bypassed into {results.Count} packets");
                                }

                                foreach (var result in results)
                                {
                                    if (result.DelayMs > 0)
                                    {
                                        await Task.Delay(result.DelayMs, cts.Token);
                                    }
                                    driver.Send(result.Data, result.RecalcChecksum);
                                }
                                continue;
                            }
                        }
                    }

                    driver.Send(packet, false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        statsTimer.Stop();

        // Print final stats
        System.Console.WriteLine();
        System.Console.WriteLine("[*] Final Statistics:");
        System.Console.WriteLine($"    Packets Processed:   {FormatNumber(Interlocked.Read(ref packetsProcessed))}");
        System.Console.WriteLine($"    Packets Modified:    {Interlocked.Read(ref modifiedPackets)}");
        System.Console.WriteLine($"    Matched Traffic:     {Interlocked.Read(ref matchedPackets)}");
        System.Console.WriteLine($"    Data Processed:      {FormatBytes(Interlocked.Read(ref bytesProcessed))}");

        var finalBw = bandwidthMonitor.GetStats();
        System.Console.WriteLine($"    Peak Download:       {BandwidthStats.FormatRate(finalBw.PeakDownload)}");
        System.Console.WriteLine($"    Peak Upload:         {BandwidthStats.FormatRate(finalBw.PeakUpload)}");

        var allBlacklisted = blacklistManager.GetAllBlacklisted();
        if (allBlacklisted.Count > 0)
        {
            System.Console.WriteLine($"\n[RADAR] Auto-detected blocked domains this session: {allBlacklisted.Count}");
        }

        return 0;
    }

    private static async Task<int> CalibrateAsync(LimboConfig config, LimboLogger logger, string[] args)
    {
        if (args.Length == 0)
        {
            System.Console.WriteLine("[!] Usage: limbo calibrate <domain> [port]");
            return 1;
        }

        var domain = args[0];
        var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 443;

        System.Console.WriteLine($"[*] Calibrating bypass for: {domain}:{port}");
        System.Console.WriteLine("[*] Testing available strategies...\n");

        var profileManager = new ProfileManager();
        var bypassEngine = BypassEngine.CreateDefault();
        var calibrator = new CalibrationEngine(profileManager, bypassEngine);

        calibrator.OnLog += msg => logger.Info(msg);
        calibrator.OnProgress += progress =>
        {
            var percent = progress.TotalStrategies > 0
                ? (progress.Progress * 100) / progress.TotalStrategies
                : 0;
            System.Console.Write($"\r[{percent,3}%] Testing {progress.CurrentMethod}...          ");
        };

        var result = await calibrator.CalibrateAsync(domain, port);

        System.Console.WriteLine("\n");

        if (result.Success)
        {
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"[+] Best method: {result.BestMethod}");
            System.Console.WriteLine($"[+] Latency:     {result.LatencyMs}ms");
            System.Console.ResetColor();

            // Offer to add profile
            System.Console.Write($"\n[?] Add profile for *.{domain}? (y/n): ");
            if (System.Console.ReadKey().KeyChar == 'y')
            {
                config.Profiles.Add(new ProfileConfig
                {
                    Domain = $"*.{domain}",
                    Method = result.BestMethod
                });
                ConfigManager.Save(config);
                System.Console.WriteLine($"\n[+] Profile added and saved to config");
            }
            else
            {
                System.Console.WriteLine();
            }
        }
        else
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"[!] {result.Message}");
            System.Console.ResetColor();
        }

        return 0;
    }

    private static async Task<int> TestConnectionAsync(LimboConfig config, string[] args)
    {
        if (args.Length == 0)
        {
            System.Console.WriteLine("[!] Usage: limbo test <domain>");
            return 1;
        }

        var domain = args[0];
        System.Console.WriteLine($"[*] Testing connection to: {domain}\n");

        // Test system DNS
        System.Console.Write("    System DNS... ");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var addresses = await System.Net.Dns.GetHostAddressesAsync(domain);
            sw.Stop();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"OK  ({addresses.Length} addr, {sw.ElapsedMilliseconds}ms)");
            System.Console.ResetColor();
            foreach (var addr in addresses.Take(3))
                System.Console.WriteLine($"               → {addr}");
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"FAIL ({ex.Message})");
            System.Console.ResetColor();
        }

        // Test DoH
        if (config.Dns.UseDoH)
        {
            System.Console.Write($"    DoH ({config.Dns.DoHServer.Replace("https://", "")})... ");
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var doh = new DoHResolver(config.Dns.DoHServer);
                var addresses = await doh.ResolveAsync(domain);
                sw.Stop();
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine($"OK  ({addresses.Length} addr, {sw.ElapsedMilliseconds}ms)");
                System.Console.ResetColor();
                foreach (var addr in addresses.Take(3))
                    System.Console.WriteLine($"               → {addr}");

                // Check if blocked
                System.Console.Write("    DNS Tamper Check... ");
                var isBlocked = await doh.IsBlockedAsync(domain);
                if (isBlocked)
                {
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.WriteLine("MISMATCH (ISP DNS may be tampered)");
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.Green;
                    System.Console.WriteLine("OK  (DNS matches DoH)");
                }
                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"FAIL ({ex.Message})");
                System.Console.ResetColor();
            }
        }

        // Test TCP:443
        System.Console.Write("    TCP:443... ");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(domain, 443);
            sw.Stop();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"OK  ({sw.ElapsedMilliseconds}ms)");
            System.Console.ResetColor();
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"FAIL ({ex.Message})");
            System.Console.ResetColor();
        }

        // Test TCP:80
        System.Console.Write("    TCP:80... ");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(domain, 80);
            sw.Stop();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"OK  ({sw.ElapsedMilliseconds}ms)");
            System.Console.ResetColor();
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"FAIL ({ex.Message})");
            System.Console.ResetColor();
        }

        return 0;
    }

    private static int AddProfile(LimboConfig config, string[] args)
    {
        if (args.Length < 2)
        {
            System.Console.WriteLine("[!] Usage: limbo add-profile <domain> <method> [options]");
            System.Console.WriteLine("    Methods: TcpFragmentation, SmartFragmentation, Desync, FakeTtl, UdpFake, Ech, QuicFake");
            System.Console.WriteLine("    Options: --frag <n>  --ttl <n>  --split <n>  --offset <n>  --ech-name <s>  --quic-size <n>");
            return 1;
        }

        var domain = args[0];
        if (!Enum.TryParse<BypassMethod>(args[1], true, out var method))
        {
            System.Console.WriteLine($"[!] Unknown method: {args[1]}");
            System.Console.WriteLine("    Use --list-methods to see available methods");
            return 1;
        }

        // Parse optional parameters
        var argList = args.Skip(2).ToList();
        var fragmentSize = GetIntArg(argList, "--frag", 2);
        var fakeTtl = GetIntArg(argList, "--ttl", 3);
        var splitPosition = GetIntArg(argList, "--split", 3);
        var splitOffset = GetIntArg(argList, "--offset", 3);
        var quicSize = GetIntArg(argList, "--quic-size", 64);
        var echName = GetStringArg(argList, "--ech-name", "");

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

        System.Console.ForegroundColor = ConsoleColor.Green;
        System.Console.WriteLine($"[+] Added profile: {domain} → {method}");
        System.Console.ResetColor();
        System.Console.WriteLine($"    FragmentSize:  {fragmentSize}");
        System.Console.WriteLine($"    FakeTtl:       {fakeTtl}");
        System.Console.WriteLine($"    SplitPosition: {splitPosition}");
        System.Console.WriteLine($"    SplitOffset:   {splitOffset}");
        if (!string.IsNullOrEmpty(echName))
            System.Console.WriteLine($"    ECH PublicName:{echName}");
        System.Console.WriteLine($"    Config saved to: {ConfigManager.GetDefaultPath()}");

        return 0;
    }

    private static int RemoveProfile(LimboConfig config, string[] args)
    {
        if (args.Length == 0)
        {
            System.Console.WriteLine("[!] Usage: limbo remove-profile <domain>");
            return 1;
        }

        var domain = args[0];
        var removed = config.Profiles.RemoveAll(p =>
            p.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            ConfigManager.Save(config);
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"[+] Removed {removed} profile(s) matching: {domain}");
            System.Console.ResetColor();
        }
        else
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"[!] No profile found matching: {domain}");
            System.Console.ResetColor();
        }

        return 0;
    }

    private static int ShowRadar()
    {
        var blacklistManager = new BlacklistManager();
        var domains = blacklistManager.GetAllBlacklisted();

        if (domains.Count == 0)
        {
            System.Console.WriteLine("[*] Radar: No auto-detected blocked domains yet.");
            System.Console.WriteLine("    Run the engine and Limbo will auto-detect blocked domains.");
        }
        else
        {
            System.Console.WriteLine($"[*] Radar: {domains.Count} auto-detected blocked domain(s):\n");
            foreach (var domain in domains)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write($"  {domain}");
                System.Console.ResetColor();
                System.Console.WriteLine(" → Adaptive");
            }
        }

        return 0;
    }

    private static int RadarImport(string[] args)
    {
        if (args.Length == 0)
        {
            System.Console.WriteLine("[!] Usage: limbo radar-import <file>");
            return 1;
        }

        var filePath = args[0];
        if (!File.Exists(filePath))
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"[!] File not found: {filePath}");
            System.Console.ResetColor();
            return 1;
        }

        try
        {
            var blacklistManager = new BlacklistManager();
            blacklistManager.Import(filePath);
            var domains = blacklistManager.GetAllBlacklisted();

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"[+] Radar: Imported {domains.Count} domain(s) from {Path.GetFileName(filePath)}");
            System.Console.ResetColor();
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"[!] Import failed: {ex.Message}");
            System.Console.ResetColor();
            return 1;
        }

        return 0;
    }

    private static int RadarExport(string[] args)
    {
        if (args.Length == 0)
        {
            System.Console.WriteLine("[!] Usage: limbo radar-export <file>");
            return 1;
        }

        var filePath = args[0];
        try
        {
            var blacklistManager = new BlacklistManager();
            var domains = blacklistManager.GetAllBlacklisted();

            if (domains.Count == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("[!] Radar: No domains to export.");
                System.Console.ResetColor();
                return 0;
            }

            blacklistManager.Export(filePath);
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"[+] Radar: Exported {domains.Count} domain(s) to {Path.GetFileName(filePath)}");
            System.Console.ResetColor();
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"[!] Export failed: {ex.Message}");
            System.Console.ResetColor();
            return 1;
        }

        return 0;
    }

    private static int RadarClear()
    {
        System.Console.Write("[?] Clear all radar-detected domains? This cannot be undone. (y/n): ");
        if (System.Console.ReadKey().KeyChar != 'y')
        {
            System.Console.WriteLine("\n[*] Cancelled.");
            return 0;
        }

        try
        {
            var autoBlacklistPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Limbo", "Auto-Blacklist.txt");

            if (File.Exists(autoBlacklistPath))
                File.Delete(autoBlacklistPath);

            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("\n[+] Radar blacklist cleared.");
            System.Console.ResetColor();
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"\n[!] Failed to clear: {ex.Message}");
            System.Console.ResetColor();
            return 1;
        }

        return 0;
    }

    private static void ShowConfig(LimboConfig config)
    {
        System.Console.WriteLine($"[*] Config file: {ConfigManager.GetDefaultPath()}\n");
        System.Console.WriteLine($"    Filter:        {config.Filter}");
        System.Console.WriteLine($"    Verbose:       {config.Verbose}");
        System.Console.WriteLine($"    Theme:         {config.Theme}");
        System.Console.WriteLine($"    MinToTray:     {config.MinimizeToTray}");
        System.Console.WriteLine($"    CloseToTray:   {config.CloseToTray}");
        System.Console.WriteLine($"    StartMinimized:{config.StartMinimized}");
        System.Console.WriteLine($"    DNS over HTTPS:{config.Dns.UseDoH}");
        if (config.Dns.UseDoH)
            System.Console.WriteLine($"    DoH Server:    {config.Dns.DoHServer}");
        System.Console.WriteLine($"\n    Profiles ({config.Profiles.Count}):");
        foreach (var p in config.Profiles)
        {
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write($"      {p.Domain,-35}");
            System.Console.ResetColor();
            System.Console.Write($" {p.Method,-20}");
            if (p.Method == BypassMethod.TcpFragmentation || p.Method == BypassMethod.SmartFragmentation)
                System.Console.Write($" frag={p.FragmentSize}");
            if (p.Method == BypassMethod.FakeTtl || p.Method == BypassMethod.UdpFake)
                System.Console.Write($" ttl={p.FakeTtl}");
            if (p.Method == BypassMethod.Desync)
                System.Console.Write($" split={p.SplitPosition}");
            if (p.Method == BypassMethod.Ech && !string.IsNullOrEmpty(p.EchPublicName))
                System.Console.Write($" ech-name={p.EchPublicName}");
            System.Console.WriteLine();
        }
    }

    private static void ListProfiles(ProfileManager manager)
    {
        var profiles = manager.GetAllProfiles();
        System.Console.WriteLine($"[*] {profiles.Count} profiles loaded:\n");

        foreach (var profile in profiles)
        {
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine($"  Domain:        {profile.Domain}");
            System.Console.ResetColor();
            System.Console.WriteLine($"  Method:        {profile.Method}");
            System.Console.WriteLine($"  FragmentSize:  {profile.FragmentSize}");
            System.Console.WriteLine($"  FakeTtl:       {profile.FakeTtl}");
            System.Console.WriteLine($"  SplitPosition: {profile.SplitPosition}");
            System.Console.WriteLine($"  SplitOffset:   {profile.SplitOffset}");
            System.Console.WriteLine($"  ECH Name:      {(string.IsNullOrEmpty(profile.EchPublicName) ? "(auto)" : profile.EchPublicName)}");
            System.Console.WriteLine($"  Verified:      {profile.IsVerified}");
            System.Console.WriteLine($"  Adaptive Params:{profile.UseAdaptiveParams}");
            System.Console.WriteLine();
        }
    }

    private static void ListMethods()
    {
        System.Console.WriteLine("[*] Available Bypass Methods:\n");

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

        System.Console.WriteLine($"  {"Name",-20} {"Priority",-10} Description");
        System.Console.WriteLine($"  {"────",-20} {"────────",-10} ───────────");
        foreach (var (name, priority, desc) in methods)
        {
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write($"  {name,-20}");
            System.Console.ResetColor();
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write($" {(priority > 0 ? priority.ToString() : "-"),-10}");
            System.Console.ResetColor();
            System.Console.WriteLine(desc);
        }

        System.Console.WriteLine("\n[*] Usage: limbo add-profile <domain> <method> [--frag <n>] [--ttl <n>]");
    }

    private static void ShowVersion()
    {
        System.Console.WriteLine($"""

            Limbo DPI Bypass Engine
            Version: 0.3.0

            Components:
              - Limbo.Core       : Bypass strategies, detection, radar
              - Limbo.Driver     : WinDivert integration
              - Limbo.Console    : Command-line interface
              - Limbo.GUI        : WPF desktop application

            Strategies:
              TCP: Fragmentation (pri:100), SmartFrag (pri:90), Desync (pri:80), FakeTTL (pri:70)
              UDP: UdpFake (pri:60), QuicFake (pri:150)
              TLS: ECH - Encrypted Client Hello (pri:200)

            Features:
              - Radar System (auto-detection of blocked domains)
              - ISP Behavior Analysis (RST injection, silent drops)
              - DNS over HTTPS (Cloudflare, Google, Quad9)
              - Bandwidth Monitoring
              - Per-domain profiles with wildcard support
              - Profile calibration

            GitHub: https://github.com/umitkrkmz/Limbo
            """);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static void LoadProfilesFromConfig(ProfileManager manager, LimboConfig config)
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

    private static string GetIpAddress(ReadOnlySpan<byte> packet, bool isIPv6, bool getSource)
    {
        try
        {
            if (!isIPv6)
            {
                var offset = getSource ? 12 : 16;
                return new System.Net.IPAddress(packet.Slice(offset, 4)).ToString();
            }
            else
            {
                var offset = getSource ? 8 : 24;
                return new System.Net.IPAddress(packet.Slice(offset, 16)).ToString();
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static int GetIntArg(List<string> args, string flag, int defaultValue)
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

    private static string GetStringArg(List<string> args, string flag, string defaultValue)
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

    private static string FormatNumber(long num) => num switch
    {
        >= 1_000_000 => $"{num / 1_000_000.0:F1}M",
        >= 1_000 => $"{num / 1_000.0:F1}K",
        _ => num.ToString()
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F1} KB",
        _ => $"{bytes} B"
    };
}
