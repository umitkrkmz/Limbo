using System.Buffers.Binary;
using System.Net;

namespace Limbo.Core.Analysis;

/// <summary>
/// Analyzes ISP behavior to detect blocking methods
/// </summary>
public class IspBehaviorAnalyzer
{
    private readonly List<BlockingEvent> _recentEvents = [];
    private readonly object _lock = new();

    // RADAR EVENT: Fired when DPI blocking (like injected RST) is actively detected
    public event Action<string>? OnBlockDetected;
    
    // RST packet patterns from different ISPs
    private static readonly Dictionary<string, byte[]> KnownIspPatterns = new()
    {
        // Add known ISP RST fingerprints here
    };

    /// <summary>
    /// Analyze an incoming RST packet to determine blocking method
    /// </summary>
    public AnalysisResult AnalyzeRstPacket(ReadOnlySpan<byte> packet, string domain)
    {
        // Check if it's a legitimate RST or injected
        var (isInjected, confidence) = DetectInjectedRst(packet);
        
        if (isInjected)
        {
            RecordEvent(new BlockingEvent
            {
                Timestamp = DateTime.UtcNow,
                Domain = domain,
                Method = BlockingMethod.SniDpi,
                RawPacket = packet.ToArray()
            });

            // RADAR TRIGGER: Notify the BlacklistManager that this CDN/Domain is blocked!
            OnBlockDetected?.Invoke(domain);
            
            return new AnalysisResult
            {
                Method = BlockingMethod.SniDpi,
                Confidence = confidence,
                Recommendation = "SNI-based DPI blocking detected. TCP fragmentation recommended.",
                SuggestedBypass = Bypass.BypassMethod.TcpFragmentation
            };
        }
        
        return new AnalysisResult
        {
            Method = BlockingMethod.None,
            Confidence = 0.5,
            Recommendation = "RST appears legitimate (server-side)."
        };
    }

    /// <summary>
    /// Analyze connection timeout pattern
    /// </summary>
    public AnalysisResult AnalyzeTimeout(string domain, int timeoutMs, int retryCount)
    {
        // Fast timeout after SYN-ACK often indicates DPI
        // Slow timeout usually means IP blocking or routing issue
        
        if (timeoutMs < 500 && retryCount == 0)
        {
            return new AnalysisResult
            {
                Method = BlockingMethod.SniDpi,
                Confidence = 0.7,
                Recommendation = "Fast timeout suggests DPI blocking after ClientHello.",
                SuggestedBypass = Bypass.BypassMethod.SmartFragmentation
            };
        }
        
        if (timeoutMs > 5000)
        {
            return new AnalysisResult
            {
                Method = BlockingMethod.IpBlocking,
                Confidence = 0.6,
                Recommendation = "Slow timeout suggests IP-level blocking. Try Adaptive mode.",
                SuggestedBypass = Bypass.BypassMethod.Adaptive
            };
        }
        
        return new AnalysisResult
        {
            Method = BlockingMethod.Unknown,
            Confidence = 0.3,
            Recommendation = "Blocking method unclear. Try all strategies.",
            SuggestedBypass = Bypass.BypassMethod.TcpFragmentation
        };
    }

    /// <summary>
    /// Check if DNS response is poisoned
    /// </summary>
    public bool IsDnsPoisoned(ReadOnlySpan<byte> dnsResponse, string expectedDomain)
    {
        // Check for common DNS poisoning indicators:
        // 1. Response too fast (< 5ms typically indicates local injection)
        // 2. Returns known block page IPs
        // 3. TTL is suspiciously low or high
        
        // This is a simplified check - real implementation would parse DNS response
        if (dnsResponse.Length < 12)
            return false;
        
        // TODO: Add DNS response parsing and known-bad IP detection
        return false;
    }

    /// <summary>
    /// Get recent blocking events for a domain
    /// </summary>
    public IReadOnlyList<BlockingEvent> GetRecentEvents(string? domain = null, int limit = 50)
    {
        lock (_lock)
        {
            var query = _recentEvents.AsEnumerable();
            if (domain != null)
                query = query.Where(e => e.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));
            return query.TakeLast(limit).ToList();
        }
    }

    /// <summary>
    /// Get statistics about blocking methods
    /// </summary>
    public Dictionary<BlockingMethod, int> GetBlockingStats()
    {
        lock (_lock)
        {
            return _recentEvents
                .GroupBy(e => e.Method)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    private (bool IsInjected, double Confidence) DetectInjectedRst(ReadOnlySpan<byte> packet)
    {
        // Injected RST packets often have:
        // 1. Zero window size
        // 2. Specific TTL values (ISP equipment)
        // 3. No TCP options
        
        if (packet.Length < 40) // IP + TCP minimum
            return (false, 0);
        
        var ipVersion = (packet[0] >> 4) & 0x0F;
        int ipHeaderLen = ipVersion == 6 ? 40 : (packet[0] & 0x0F) * 4;
        
        if (packet.Length < ipHeaderLen + 20)
            return (false, 0);
        
        // Check TCP flags - should have RST (0x04)
        var tcpFlags = packet[ipHeaderLen + 13];
        if ((tcpFlags & 0x04) == 0)
            return (false, 0);
        
        // Check window size (bytes 14-15 of TCP header)
        var windowSize = BinaryPrimitives.ReadUInt16BigEndian(packet[(ipHeaderLen + 14)..]);
        
        // Check TTL (byte 8 of IPv4 header)
        var ttl = ipVersion == 4 ? packet[8] : packet[7]; // Hop limit for IPv6
        
        // Heuristics for injected RST:
        // - Window size is 0 (common in injected RST)
        // - TTL is an unusual value for the route
        
        double confidence = 0;
        
        if (windowSize == 0)
            confidence += 0.4;
        
        // Common ISP equipment TTL values
        if (ttl == 64 || ttl == 128 || ttl == 255)
            confidence += 0.2;
        
        // RST+ACK is more suspicious than just RST
        if ((tcpFlags & 0x14) == 0x14)
            confidence += 0.2;
        
        return (confidence >= 0.5, confidence);
    }

    private void RecordEvent(BlockingEvent evt)
    {
        lock (_lock)
        {
            _recentEvents.Add(evt);
            
            // Keep only last 1000 events
            while (_recentEvents.Count > 1000)
                _recentEvents.RemoveAt(0);
        }
    }
}
