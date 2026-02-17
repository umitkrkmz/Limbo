using Limbo.Core.Analysis;
using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Limbo.Core.Detection;
using Limbo.Core.Dns;
using Limbo.Core.Profiles;

namespace Limbo.Core.Engine;

/// <summary>
/// Main Limbo engine that orchestrates DPI bypass
/// </summary>
public class LimboEngine : IDisposable
{
    private readonly ProfileManager _profileManager;
    private readonly BypassEngine _bypassEngine;
    private readonly IspBehaviorAnalyzer _analyzer;
    private readonly BlacklistManager _blacklistManager; // NEW: Radar System

    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    private DoHResolver? _dohResolver;
    private DnsSettings? _dnsSettings;

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

    // Expose BlacklistManager so GUI/Console can trigger Import/Export
    public BlacklistManager AutoBlacklist => _blacklistManager;

    public LimboEngine(ProfileManager? profileManager = null, BypassEngine? bypassEngine = null)
    {
        _profileManager = profileManager ?? new ProfileManager();
        _bypassEngine = bypassEngine ?? BypassEngine.CreateDefault();
        _analyzer = new IspBehaviorAnalyzer();
        
        _bypassEngine.OnLog += msg => OnLog?.Invoke(msg);

        // Initialize the Radar System
        _blacklistManager = new BlacklistManager();

        // 1. Wire up Analyzer to BlacklistManager (Sensor to Storage)
        _analyzer.OnBlockDetected += domain => 
        {
            _blacklistManager.AddToBlacklist(domain);
        };

        // 2. Wire up BlacklistManager to ProfileManager (Storage to Active Engine)
        _blacklistManager.OnNewBlacklistedDomain += domain =>
        {
            var newProfile = new DomainProfile 
            { 
                Domain = domain, 
                Method = BypassMethod.Adaptive, // Let calibration find the best way
                IsVerified = false
            };
            
            _profileManager.SaveProfile(newProfile);
            OnLog?.Invoke($"[RADAR] Auto-Blacklisted and applied Adaptive bypass: {domain}");
        };

        // 3. Load existing Auto-Blacklist.txt into active profiles on engine startup
        LoadBlacklistToProfiles();

        // Test için sahte bir tetikleme
_blacklistManager.AddToBlacklist("test-engellenen-site.com");
    }

    /// <summary>
    /// Applies existing blacklisted domains as Adaptive profiles on startup
    /// </summary>
    private void LoadBlacklistToProfiles()
    {
        var blacklisted = _blacklistManager.GetAllBlacklisted();
        int count = 0;
        
        foreach (var domain in blacklisted)
        {
            // Only add if user hasn't explicitly set a custom profile for this exact domain
            var existing = _profileManager.GetProfile(domain);
            if (existing.Method == BypassMethod.None) 
            {
                _profileManager.SaveProfile(new DomainProfile 
                { 
                    Domain = domain, 
                    Method = BypassMethod.Adaptive 
                });
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
    /// Start the engine with provided packet capture functions
    /// </summary>
    public void Start(
        Func<string, nint> openHandle,
        Func<nint, byte[], Action<byte[], bool>, bool> captureLoop,
        Action<nint, byte[], bool>? sendPacket,
        string filter = "tcp or udp")
    {
        lock (_lock)
        {
            if (_running) throw new InvalidOperationException("Engine is already running");
            _running = true;
        }

        OnLog?.Invoke($"Starting Limbo engine with filter: {filter}");
        OnLog?.Invoke($"Loaded {_profileManager.GetAllProfiles().Count} domain profiles");

        _captureTask = Task.Run(() => RunCaptureLoop(openHandle, captureLoop, sendPacket, filter));
    }

    /// <summary>
    /// Start the engine with DoH pre-resolution for profile domains
    /// </summary>
    public async Task StartAsync(
        Func<string, nint> openHandle,
        Func<nint, byte[], Action<byte[], bool>, bool> captureLoop,
        Action<nint, byte[], bool>? sendPacket,
        string filter = "tcp or udp")
    {
        // Pre-resolve domains using DoH if enabled
        if (_dnsSettings?.UseDoH == true)
        {
            await PreResolveDomains();
        }

        Start(openHandle, captureLoop, sendPacket, filter);
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
    /// Process a single packet and return modified packets if needed
    /// </summary>
    public IReadOnlyList<PacketResult> ProcessPacket(ReadOnlySpan<byte> packet, bool isOutbound)
    {
        Interlocked.Increment(ref _packetsProcessed);
        Interlocked.Add(ref _bytesProcessed, packet.Length);

        // Analyze the packet
        var context = PacketInspector.Analyze(packet, isOutbound);

        // Only process outbound packets with identified domains
        if (!isOutbound || context.TargetDomain == null)
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

    private void RunCaptureLoop(
        Func<string, nint> openHandle,
        Func<nint, byte[], Action<byte[], bool>, bool> captureLoop,
        Action<nint, byte[], bool>? sendPacket,
        string filter)
    {
        nint handle = nint.Zero;

        try
        {
            handle = openHandle(filter);

            if (handle == nint.Zero || handle == -1)
            {
                OnLog?.Invoke("Failed to open packet capture handle");
                return;
            }

            OnLog?.Invoke("Packet capture handle opened successfully");

            var buffer = new byte[65535];

            while (!_cts.Token.IsCancellationRequested)
            {
                var success = captureLoop(handle, buffer, (packet, isOutbound) =>
                {
                    var results = ProcessPacket(packet, isOutbound);
                    
                    // Send modified packets if we have a send function
                    if (sendPacket != null)
                    {
                        foreach (var result in results)
                        {
                            sendPacket(handle, result.Data, result.RecalcChecksum);
                        }
                    }
                });

                if (!success && !_cts.Token.IsCancellationRequested)
                {
                    OnLog?.Invoke("Capture loop returned false");
                    break;
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
        _captureTask?.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
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
