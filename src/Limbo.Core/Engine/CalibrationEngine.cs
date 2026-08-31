using System.Net;
using Limbo.Core.Bypass;
using Limbo.Core.Detection;
using Limbo.Core.Profiles;
using Limbo.Driver;

namespace Limbo.Core.Engine;

/// <summary>
/// Calibration engine - automatically finds working bypass method for a domain.
/// Each strategy is tested by actually applying it (via a scoped WinDivert
/// interception of just this test connection's traffic), not simulated -
/// this requires Administrator privileges the same way `run` does. Without
/// them, calibration still works but can't tell strategies apart from a
/// plain connectivity check, and says so.
/// Inspired by ZeroDivert's AdaptiveEngine
/// </summary>
public class CalibrationEngine
{
    private readonly ProfileManager _profileManager;
    private readonly BypassEngine _bypassEngine;

    public event Action<string>? OnLog;
    public event Action<CalibrationProgress>? OnProgress;

    public CalibrationEngine(ProfileManager profileManager, BypassEngine bypassEngine)
    {
        _profileManager = profileManager;
        _bypassEngine = bypassEngine;
    }

    /// <summary>
    /// Calibrate bypass method for a specific domain
    /// </summary>
    public async Task<CalibrationResult> CalibrateAsync(
        string domain,
        int port = 443,
        CancellationToken ct = default)
    {
        OnLog?.Invoke($"Starting calibration for {domain}:{port}");

        // Only TCP-capable strategies can be tested against a TLS ClientHello
        // connectivity check. UDP/QUIC strategies (UdpFake, QuicFake) need a
        // UDP-based test we don't have yet, so they're excluded rather than
        // silently "tested" against the wrong protocol.
        var strategies = _bypassEngine.GetStrategiesInOrder().Where(s => s.SupportsTcp).ToList();

        var targetIp = await NetworkProbe.ResolveIPv4Async(domain, ct);
        var canInterceptTraffic = targetIp != null && NetworkProbe.CanOpenWinDivert();

        if (targetIp == null)
        {
            OnLog?.Invoke($"Could not resolve an IPv4 address for {domain} - strategies cannot be applied, results will only reflect plain connectivity");
        }
        else if (!canInterceptTraffic)
        {
            OnLog?.Invoke("Administrator privileges required to actually apply bypass strategies during calibration - falling back to a plain connectivity check for every strategy");
        }

        var results = new List<(BypassMethod Method, bool Success, int LatencyMs)>();

        // Test without any bypass first (baseline)
        OnProgress?.Invoke(new CalibrationProgress
        {
            Domain = domain,
            CurrentMethod = BypassMethod.None,
            Progress = 0,
            TotalStrategies = strategies.Count + 1
        });

        var baselineResult = targetIp != null
            ? await NetworkProbe.TestConnectionAsync(targetIp, domain, port, ct)
            : (Success: false, LatencyMs: 0, FailureReason: "No IPv4 address to test");
        results.Add((BypassMethod.None, baselineResult.Success, baselineResult.LatencyMs));

        if (!baselineResult.Success)
        {
            OnLog?.Invoke($"Baseline check failed: {baselineResult.FailureReason}");
        }

        if (baselineResult.Success)
        {
            OnLog?.Invoke($"Direct connection works for {domain} (no bypass needed)");

            // Save as no-bypass profile
            var profile = new DomainProfile
            {
                Domain = domain,
                Method = BypassMethod.None,
                LastCalibrated = DateTime.UtcNow,
                SuccessRate = 100,
                IsVerified = true
            };
            _profileManager.SaveProfile(profile);

            return new CalibrationResult
            {
                Domain = domain,
                Success = true,
                BestMethod = BypassMethod.None,
                LatencyMs = baselineResult.LatencyMs,
                Message = "Direct connection works, no bypass needed"
            };
        }

        // Test each strategy
        var strategyIndex = 1;
        BypassMethod bestMethod = BypassMethod.None;
        int bestLatency = int.MaxValue;

        foreach (var strategy in strategies)
        {
            var method = GetMethodFromStrategy(strategy);

            OnProgress?.Invoke(new CalibrationProgress
            {
                Domain = domain,
                CurrentMethod = method,
                Progress = strategyIndex,
                TotalStrategies = strategies.Count + 1
            });

            OnLog?.Invoke($"Testing {strategy.Name}...");

            var testResult = canInterceptTraffic
                ? await TestWithBypassAsync(domain, targetIp!, port, strategy, ct)
                : targetIp != null
                    ? await NetworkProbe.TestConnectionAsync(targetIp, domain, port, ct)
                    : (Success: false, LatencyMs: 0, FailureReason: "No IPv4 address to test");
            results.Add((method, testResult.Success, testResult.LatencyMs));

            if (testResult.Success && testResult.LatencyMs < bestLatency)
            {
                bestMethod = method;
                bestLatency = testResult.LatencyMs;
                OnLog?.Invoke($"  ✓ {strategy.Name} works! Latency: {testResult.LatencyMs}ms");
            }
            else if (testResult.Success)
            {
                OnLog?.Invoke($"  ✓ {strategy.Name} works but slower ({testResult.LatencyMs}ms)");
            }
            else
            {
                OnLog?.Invoke($"  ✗ {strategy.Name} failed ({testResult.FailureReason})");
            }

            strategyIndex++;
        }

        if (bestMethod != BypassMethod.None)
        {
            // Save successful profile
            var profile = new DomainProfile
            {
                Domain = domain,
                Method = bestMethod,
                FragmentSize = GetDefaultFragmentSize(bestMethod),
                LastCalibrated = DateTime.UtcNow,
                SuccessRate = 100,
                IsVerified = true
            };
            _profileManager.SaveProfile(profile);

            return new CalibrationResult
            {
                Domain = domain,
                Success = true,
                BestMethod = bestMethod,
                LatencyMs = bestLatency,
                Message = $"Best method: {bestMethod} ({bestLatency}ms)"
            };
        }

        // All strategies failed
        return new CalibrationResult
        {
            Domain = domain,
            Success = false,
            BestMethod = BypassMethod.Adaptive,
            LatencyMs = 0,
            Message = "All DPI bypass methods failed. IP-based blocking suspected. Try Adaptive mode."
        };
    }

    /// <summary>
    /// Quick test to check if a domain is blocked
    /// </summary>
    public async Task<bool> IsBlockedAsync(string domain, int port = 443, CancellationToken ct = default)
    {
        var targetIp = await NetworkProbe.ResolveIPv4Async(domain, ct);
        if (targetIp == null) return true; // can't even resolve - treat as blocked

        var result = await NetworkProbe.TestConnectionAsync(targetIp, domain, port, ct);
        return !result.Success;
    }

    /// <summary>
    /// Tests a domain with a specific strategy actually applied: opens a
    /// WinDivert handle scoped to this connection's IP/port, intercepts the
    /// outbound TLS ClientHello and runs it through the strategy exactly
    /// like `run` would, forwards everything else untouched, then performs
    /// the same connectivity check as the baseline.
    /// </summary>
    private async Task<(bool Success, int LatencyMs, string? FailureReason)> TestWithBypassAsync(
        string domain,
        IPAddress targetIp,
        int port,
        IDpiBypassStrategy strategy,
        CancellationToken ct)
    {
        using var driver = new WinDivertDriver();
        var filter = $"tcp and (tcp.DstPort == {port} or tcp.SrcPort == {port}) and (ip.DstAddr == {targetIp} or ip.SrcAddr == {targetIp})";

        if (!driver.Open(filter))
        {
            return await NetworkProbe.TestConnectionAsync(targetIp, domain, port, ct);
        }

        var interceptTask = Task.Run(() => InterceptAndApplyStrategy(driver, strategy, ct), CancellationToken.None);

        try
        {
            return await NetworkProbe.TestConnectionAsync(targetIp, domain, port, ct);
        }
        finally
        {
            // Closing the handle unblocks the interceptor's pending WinDivertRecv,
            // the same shutdown pattern RunCommand uses for the live capture loop.
            driver.Dispose();
            await interceptTask;
        }
    }

    private static void InterceptAndApplyStrategy(WinDivertDriver driver, IDpiBypassStrategy strategy, CancellationToken ct)
    {
        var buffer = new byte[65535];

        while (!ct.IsCancellationRequested)
        {
            if (!driver.Receive(buffer, out var length, out var isOutbound))
            {
                if (!driver.IsOpen) return; // handle closed by the caller - test is over
                continue;
            }

            var packet = buffer.AsSpan(0, length);
            var context = PacketInspector.Analyze(packet, isOutbound);

            if (isOutbound && context.IsClientHello)
            {
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

                foreach (var result in strategy.Process(packet, packetContext))
                {
                    driver.Send(result.Data, result.RecalcChecksum);
                }
            }
            else
            {
                driver.Send(packet, false);
            }
        }
    }

    private static BypassMethod GetMethodFromStrategy(IDpiBypassStrategy strategy)
    {
        return strategy.Name switch
        {
            "TCP Fragmentation" => BypassMethod.TcpFragmentation,
            "Smart Fragmentation" => BypassMethod.SmartFragmentation,
            "TCP Desync" => BypassMethod.Desync,
            "Fake TTL" => BypassMethod.FakeTtl,
            "UDP Fake" => BypassMethod.UdpFake,
            "ECH (Encrypted Client Hello)" => BypassMethod.Ech,
            _ => BypassMethod.None
        };
    }

    private static int GetDefaultFragmentSize(BypassMethod method)
    {
        return method switch
        {
            BypassMethod.TcpFragmentation => 2,
            BypassMethod.SmartFragmentation => 3,
            _ => 0
        };
    }
}

/// <summary>
/// Calibration progress information
/// </summary>
public record CalibrationProgress
{
    public required string Domain { get; init; }
    public required BypassMethod CurrentMethod { get; init; }
    public required int Progress { get; init; }
    public required int TotalStrategies { get; init; }
}

/// <summary>
/// Calibration result
/// </summary>
public record CalibrationResult
{
    public required string Domain { get; init; }
    public required bool Success { get; init; }
    public required BypassMethod BestMethod { get; init; }
    public required int LatencyMs { get; init; }
    public required string Message { get; init; }
}
