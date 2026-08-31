using System.Collections.Concurrent;
using Limbo.Core.Detection;
using Limbo.Core.Dns;
using Limbo.Core.Engine;
using Limbo.Driver;

namespace Limbo.Core.Analysis;

/// <summary>
/// Observes (never modifies) real traffic to a domain through a scoped
/// WinDivert session and reports what the network actually did: DNS
/// tampering (system DNS vs DoH mismatch), RST injection, or silent
/// dropping. Requires Administrator the same way `run`/calibration do -
/// without it, only the (no-admin-required) DNS tamper check runs.
///
/// This is what finally wires IspBehaviorAnalyzer.AnalyzeRstPacket to real
/// inbound packets - nothing in LimboEngine's live capture loop calls it, so
/// RST-injection detection has never actually fired outside of this scan.
/// </summary>
public class NetworkAnalyzer
{
    public event Action<string>? OnLog;

    public async Task<DomainAnalysisResult> AnalyzeDomainAsync(string domain, int port = 443, CancellationToken ct = default)
    {
        OnLog?.Invoke($"Analyzing {domain}:{port}...");

        bool dnsTamperSuspected;
        using (var doh = new DoHResolver())
        {
            dnsTamperSuspected = await doh.IsBlockedAsync(domain, ct);
        }

        var targetIp = await NetworkProbe.ResolveIPv4Async(domain, ct);
        if (targetIp == null)
        {
            OnLog?.Invoke($"Could not resolve an IPv4 address for {domain}");
            return new DomainAnalysisResult
            {
                Domain = domain,
                DnsTamperSuspected = dnsTamperSuspected,
                Outcome = NetworkAnalysisOutcome.CouldNotResolveDns
            };
        }

        if (!NetworkProbe.CanOpenWinDivert())
        {
            return new DomainAnalysisResult
            {
                Domain = domain,
                DnsTamperSuspected = dnsTamperSuspected,
                Outcome = NetworkAnalysisOutcome.RequiresAdministrator
            };
        }

        using var driver = new WinDivertDriver();
        var filter = $"tcp and (tcp.DstPort == {port} or tcp.SrcPort == {port}) and (ip.DstAddr == {targetIp} or ip.SrcAddr == {targetIp})";

        if (!driver.Open(filter))
        {
            return new DomainAnalysisResult
            {
                Domain = domain,
                DnsTamperSuspected = dnsTamperSuspected,
                Outcome = NetworkAnalysisOutcome.RequiresAdministrator
            };
        }

        var analyzer = new IspBehaviorAnalyzer();
        using var tracker = new ConnectionTracker();

        var silentDrops = new ConcurrentBag<string>();
        tracker.OnSilentDropDetected += d => silentDrops.Add(d);

        var observeTask = Task.Run(() => ObserveTraffic(driver, domain, analyzer, tracker, ct), CancellationToken.None);

        bool connectionSucceeded;
        string? failureReason = null;
        try
        {
            var probe = await NetworkProbe.TestConnectionAsync(targetIp, domain, port, ct);
            connectionSucceeded = probe.Success;
            failureReason = probe.FailureReason;

            if (!connectionSucceeded)
            {
                // Give ConnectionTracker's sweeper (1s interval, 3s silent-drop
                // threshold) a chance to fire before tearing the session down -
                // its threshold matches our own probe's read timeout, so
                // disposing immediately after would race its next tick.
                await Task.Delay(1200, CancellationToken.None);
            }
        }
        finally
        {
            // Closing the handle unblocks the observer's pending WinDivertRecv,
            // the same shutdown pattern used throughout this codebase.
            driver.Dispose();
        }

        var observed = await observeTask;

        var rstDetected = analyzer.GetRecentEvents(domain).Count > 0;
        var silentDropDetected = !silentDrops.IsEmpty;

        var outcome = connectionSucceeded
            ? NetworkAnalysisOutcome.Reachable
            : rstDetected
                ? NetworkAnalysisOutcome.RstInjectionDetected
                : silentDropDetected
                    ? NetworkAnalysisOutcome.SilentlyDropped
                    : NetworkAnalysisOutcome.Unreachable;

        OnLog?.Invoke(
            $"{domain}: {outcome} (probe: {(connectionSucceeded ? "ok" : failureReason)}; " +
            $"observed {observed.Inbound} inbound / {observed.Outbound} outbound packet(s) on the scoped filter)");

        return new DomainAnalysisResult
        {
            Domain = domain,
            DnsTamperSuspected = dnsTamperSuspected,
            Outcome = outcome
        };
    }

    /// <summary>Returns how many inbound/outbound packets matched the scoped filter - a direct signal of whether WinDivert was watching the right traffic at all.</summary>
    private static (int Inbound, int Outbound) ObserveTraffic(
        WinDivertDriver driver,
        string domain,
        IspBehaviorAnalyzer analyzer,
        ConnectionTracker tracker,
        CancellationToken ct)
    {
        var buffer = new byte[65535];
        var inboundCount = 0;
        var outboundCount = 0;

        while (!ct.IsCancellationRequested)
        {
            if (!driver.Receive(buffer, out var length, out var isOutbound))
            {
                if (!driver.IsOpen) break; // handle closed by the caller - scan is over
                continue;
            }

            if (isOutbound) outboundCount++; else inboundCount++;

            var packet = buffer.AsSpan(0, length);
            var context = PacketInspector.Analyze(packet, isOutbound);

            if (!isOutbound)
            {
                var srcIp = GetIpAddress(packet, context.IsIPv6, getSource: true);
                tracker.RegisterInbound(srcIp, context.SrcPort);

                if (context.IsTcp && IsRstPacket(packet, context.IsIPv6))
                {
                    analyzer.AnalyzeRstPacket(packet, domain);
                }
            }
            else if ((context.IsClientHello || context.HasHttpHost) && !string.IsNullOrEmpty(context.TargetDomain))
            {
                var dstIp = GetIpAddress(packet, context.IsIPv6, getSource: false);
                tracker.TrackOutbound(dstIp, context.DstPort, context.TargetDomain);
            }

            // Pure observation - never modify or drop, just forward unchanged.
            driver.Send(packet, false);
        }

        return (inboundCount, outboundCount);
    }

    private static bool IsRstPacket(ReadOnlySpan<byte> packet, bool isIPv6)
    {
        var ipHeaderLen = isIPv6 ? 40 : (packet[0] & 0x0F) * 4;
        if (packet.Length < ipHeaderLen + 14) return false;

        var tcpFlags = packet[ipHeaderLen + 13];
        return (tcpFlags & 0x04) != 0; // RST flag
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
}

public enum NetworkAnalysisOutcome
{
    Reachable,
    RstInjectionDetected,
    SilentlyDropped,
    Unreachable,
    CouldNotResolveDns,
    RequiresAdministrator
}

public record DomainAnalysisResult
{
    public required string Domain { get; init; }
    public required bool DnsTamperSuspected { get; init; }
    public required NetworkAnalysisOutcome Outcome { get; init; }
}
