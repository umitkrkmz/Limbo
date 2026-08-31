using Limbo.Core.Analysis;
using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Limbo.Core.Detection;
using Limbo.Core.Dns;
using Limbo.Core.Monitoring;
using Limbo.Core.Profiles;
using Limbo.Driver;

namespace Limbo.Core.Engine;

/// <summary>
/// Main Limbo engine that orchestrates DPI bypass: owns the packet capture
/// loop against an <see cref="IPacketDriver"/>, profile lookup, bypass
/// strategy application, Radar auto-detection, connection/bandwidth
/// tracking, and DoH pre-resolution.
/// </summary>
public class LimboEngine : IDisposable
{
    private readonly ProfileManager _profileManager;
    private readonly BypassEngine _bypassEngine;
    private readonly IspBehaviorAnalyzer _analyzer;
    private readonly BlacklistManager _blacklistManager; // Radar System
    private readonly ConnectionTracker _tracker = new();
    private readonly BandwidthMonitor _bandwidthMonitor = new();
    private readonly ProfileConfig _defaultMethodProfile;

    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    private DoHResolver? _dohResolver;
    private DnsSettings? _dnsSettings;

    private IPacketDriver? _driver;
    private bool _running;
    private bool _disposed;
    private Task? _captureTask;

    // Statistics
    private long _packetsProcessed;
    private long _packetsModified;
    private long _bytesProcessed;
    private long _matchedPackets;

    public event Action<string>? OnLog;
    public event Action<EngineStats>? OnStats;

    public bool IsRunning => _running;
    public ProfileManager Profiles => _profileManager;
    public IspBehaviorAnalyzer Analyzer => _analyzer;
    public BandwidthMonitor Bandwidth => _bandwidthMonitor;

    // Expose BlacklistManager so callers can trigger Import/Export
    public BlacklistManager AutoBlacklist => _blacklistManager;

    /// <param name="enableRadar">
    /// Whether to wire up automatic ISP-block detection and auto-profile
    /// creation. When false, the engine still applies manually configured
    /// profiles, it just never adds new ones on its own.
    /// </param>
    /// <param name="defaultMethodProfile">
    /// Method/parameter template applied to domains Radar auto-detects.
    /// Defaults to Adaptive with adaptive parameters if not given.
    /// </param>
    public LimboEngine(
        ProfileManager? profileManager = null,
        BypassEngine? bypassEngine = null,
        bool enableRadar = true,
        ProfileConfig? defaultMethodProfile = null)
    {
        _profileManager = profileManager ?? new ProfileManager();
        _bypassEngine = bypassEngine ?? BypassEngine.CreateDefault();
        _analyzer = new IspBehaviorAnalyzer();
        _blacklistManager = new BlacklistManager();
        _defaultMethodProfile = defaultMethodProfile ?? new ProfileConfig { Domain = "*", Method = BypassMethod.Adaptive };

        _bypassEngine.OnLog += msg => OnLog?.Invoke(msg);

        if (enableRadar)
        {
            WireUpRadar();
        }
    }

    private void WireUpRadar()
    {
        // 1. Wire up Analyzer to BlacklistManager (Sensor to Storage)
        _analyzer.OnBlockDetected += domain => _blacklistManager.AddToBlacklist(domain);

        // 2. Wire up BlacklistManager to ProfileManager (Storage to Active Engine)
        _blacklistManager.OnNewBlacklistedDomain += domain =>
        {
            _profileManager.SaveProfile(BuildDefaultMethodProfile(domain));
            OnLog?.Invoke($"[RADAR] Auto-Blacklisted and applied {_defaultMethodProfile.Method} bypass: {domain}");
        };

        // 3. Load existing Auto-Blacklist.txt into active profiles on engine startup
        LoadBlacklistToProfiles();
    }

    private DomainProfile BuildDefaultMethodProfile(string domain) => new()
    {
        Domain = domain,
        Method = _defaultMethodProfile.Method,
        UseAdaptiveParams = _defaultMethodProfile.UseAdaptiveParams,
        FragmentSize = _defaultMethodProfile.FragmentSize,
        SplitPosition = _defaultMethodProfile.SplitPosition,
        SplitOffset = _defaultMethodProfile.SplitOffset,
        FakeTtl = _defaultMethodProfile.FakeTtl,
        EchPublicName = _defaultMethodProfile.EchPublicName,
        FakePacketSize = _defaultMethodProfile.FakePacketSize,
        IsVerified = false
    };

    /// <summary>
    /// Applies existing blacklisted domains as profiles on startup
    /// </summary>
    private void LoadBlacklistToProfiles()
    {
        var blacklisted = _blacklistManager.GetAllBlacklisted();
        var count = 0;

        foreach (var domain in blacklisted)
        {
            // Only add if user hasn't explicitly set a custom profile for this exact domain
            var existing = _profileManager.GetProfile(domain);
            if (existing.Method == BypassMethod.None)
            {
                _profileManager.SaveProfile(BuildDefaultMethodProfile(domain));
                count++;
            }
        }

        if (count > 0)
        {
            OnLog?.Invoke($"[RADAR] Loaded {count} domains from Auto-Blacklist.txt into engine");
        }
    }

    /// <summary>
    /// Configure DNS settings (call before Start)
    /// </summary>
    public void ConfigureDns(DnsSettings settings)
    {
        _dnsSettings = settings;
    }

    /// <summary>
    /// Start the engine against an already-open packet driver.
    /// </summary>
    public void Start(IPacketDriver driver, string filter = "tcp or udp")
    {
        lock (_lock)
        {
            if (_running) throw new InvalidOperationException("Engine is already running");
            _running = true;
        }

        _driver = driver;

        OnLog?.Invoke($"Starting Limbo engine with filter: {filter}");
        OnLog?.Invoke($"Loaded {_profileManager.GetAllProfiles().Count} domain profiles");

        _captureTask = Task.Run(() => RunCaptureLoopAsync(driver));
    }

    /// <summary>
    /// Start the engine with DoH pre-resolution for profile domains
    /// </summary>
    public async Task StartAsync(IPacketDriver driver, string filter = "tcp or udp")
    {
        // Pre-resolve domains using DoH if enabled
        if (_dnsSettings?.UseDoH == true)
        {
            await PreResolveDomains();
        }

        Start(driver, filter);
    }

    /// <summary>
    /// Pre-resolve all profile domains using DoH
    /// </summary>
    private async Task PreResolveDomains()
    {
        if (_dnsSettings == null || !_dnsSettings.UseDoH)
            return;

        var serverUrl = string.IsNullOrEmpty(_dnsSettings.DoHServer)
            ? "https://cloudflare-dns.com/dns-query"
            : _dnsSettings.DoHServer;

        OnLog?.Invoke($"[DoH] Initializing resolver with: {serverUrl}");
        _dohResolver = new DoHResolver(serverUrl);
        _dohResolver.OnLog += msg => OnLog?.Invoke(msg);

        var profiles = _profileManager.GetAllProfiles();
        var domains = profiles
            .Select(p => p.Domain)
            .Where(d => !d.StartsWith("*")) // Skip wildcards
            .Distinct()
            .ToList();

        if (domains.Count == 0)
        {
            OnLog?.Invoke("[DoH] No domains to pre-resolve");
            return;
        }

        OnLog?.Invoke($"[DoH] Pre-resolving {domains.Count} domain(s)...");

        try
        {
            var results = await _dohResolver.ResolveBulkAsync(domains, _cts.Token);
            var resolved = results.Count(r => r.Value.Length > 0);
            OnLog?.Invoke($"[DoH] Pre-resolution complete: {resolved}/{domains.Count} resolved");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[DoH] Pre-resolution error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the DoH resolver (for testing DNS resolution)
    /// </summary>
    public DoHResolver? DohResolver => _dohResolver;

    /// <summary>
    /// Process a single packet and return modified packet(s) if needed
    /// </summary>
    public IReadOnlyList<PacketResult> ProcessPacket(ReadOnlySpan<byte> packet, bool isOutbound)
    {
        var context = PacketInspector.Analyze(packet, isOutbound);
        return ProcessPacket(packet, context);
    }

    /// <summary>Same as the public overload, but reuses a context the caller already computed.</summary>
    private IReadOnlyList<PacketResult> ProcessPacket(ReadOnlySpan<byte> packet, PacketContext context)
    {
        Interlocked.Increment(ref _packetsProcessed);
        Interlocked.Add(ref _bytesProcessed, packet.Length);

        // Only process outbound packets with identified domains
        if (!context.IsOutbound || context.TargetDomain == null)
            return [PacketResult.FromOriginal(packet)];

        // Get profile for this domain
        var profile = _profileManager.GetProfile(context.TargetDomain);

        if (profile.Method == BypassMethod.None)
            return [PacketResult.FromOriginal(packet)];

        Interlocked.Increment(ref _matchedPackets);

        // Apply bypass strategy
        var results = _bypassEngine.ProcessPacket(packet, context, profile.Method);

        if (results.Count > 1 || (results.Count == 1 && results[0].RecalcChecksum))
        {
            Interlocked.Increment(ref _packetsModified);
        }

        return results;
    }

    private async Task RunCaptureLoopAsync(IPacketDriver driver)
    {
        var buffer = new byte[65535];

        try
        {
            OnLog?.Invoke("Packet capture handle opened successfully");

            while (!_cts.Token.IsCancellationRequested)
            {
                if (!driver.Receive(buffer, out var length, out var isOutbound))
                {
                    if (!driver.IsOpen) break; // handle was closed to unblock us - shut down
                    continue;
                }

                var packet = buffer.AsSpan(0, length);
                var context = PacketInspector.Analyze(packet, isOutbound);
                _bandwidthMonitor.RecordBytes(length, !isOutbound);

                if (!isOutbound)
                {
                    var srcIp = GetIpAddress(packet, context.IsIPv6, getSource: true);
                    _tracker.RegisterInbound(srcIp, context.SrcPort);
                }
                else if ((context.IsClientHello || context.HasHttpHost) && !string.IsNullOrEmpty(context.TargetDomain))
                {
                    var dstIp = GetIpAddress(packet, context.IsIPv6, getSource: false);
                    _tracker.TrackOutbound(dstIp, context.DstPort, context.TargetDomain);
                }

                var results = ProcessPacket(packet, context);

                foreach (var result in results)
                {
                    if (result.DelayMs > 0)
                        await Task.Delay(result.DelayMs, _cts.Token);
                    driver.Send(result.Data, result.RecalcChecksum);
                }

                // Report stats periodically
                if (_packetsProcessed % 1000 == 0)
                {
                    OnStats?.Invoke(GetStats());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Capture error: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _running = false;
            }
            OnLog?.Invoke("Capture loop ended");
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

            var offset6 = getSource ? 8 : 24;
            return new System.Net.IPAddress(packet.Slice(offset6, 16)).ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    public EngineStats GetStats() => new()
    {
        PacketsProcessed = Interlocked.Read(ref _packetsProcessed),
        PacketsModified = Interlocked.Read(ref _packetsModified),
        BytesProcessed = Interlocked.Read(ref _bytesProcessed),
        MatchedPackets = Interlocked.Read(ref _matchedPackets),
        ProfileCount = _profileManager.GetAllProfiles().Count
    };

    public void Stop()
    {
        _cts.Cancel();
        _driver?.Dispose(); // unblocks the pending (blocking) WinDivertRecv call
        _captureTask?.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _tracker.Dispose();
        _dohResolver?.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Engine statistics
/// </summary>
public readonly struct EngineStats
{
    public required long PacketsProcessed { get; init; }
    public required long PacketsModified { get; init; }
    public required long BytesProcessed { get; init; }
    public required long MatchedPackets { get; init; }
    public required int ProfileCount { get; init; }
}
