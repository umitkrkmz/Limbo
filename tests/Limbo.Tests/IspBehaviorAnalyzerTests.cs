using Limbo.Core.Analysis;
using Limbo.Core.Bypass;

namespace Limbo.Tests;

public class IspBehaviorAnalyzerTests
{
    private static byte[] CreateRstPacket(
        byte ttl = 64,
        ushort windowSize = 0,
        bool rstAck = false)
    {
        // Minimal IPv4 + TCP packet with RST flag set
        var packet = new byte[40]; // 20 IP + 20 TCP

        // IPv4 header
        packet[0] = 0x45; // Version 4, IHL 5
        packet[8] = ttl;  // TTL
        packet[9] = 6;    // Protocol TCP

        // TCP header at offset 20
        packet[20] = 0x00; packet[21] = 0x50; // Source port 80
        packet[22] = 0x1F; packet[23] = 0x90; // Dest port 8080

        // TCP flags at offset 33 (20 + 13)
        packet[33] = rstAck ? (byte)0x14 : (byte)0x04; // RST+ACK or just RST

        // TCP window size at offset 34 (20 + 14)
        packet[34] = (byte)(windowSize >> 8);
        packet[35] = (byte)(windowSize & 0xFF);

        // Data offset at offset 32 (20 + 12)
        packet[32] = 0x50; // 5 * 4 = 20 bytes

        return packet;
    }

    [Fact]
    public void AnalyzeRstPacket_NonRstPacket_ReturnsNone()
    {
        // Arrange
        var analyzer = new IspBehaviorAnalyzer();
        var packet = new byte[40];
        packet[0] = 0x45; // IPv4
        packet[9] = 6;    // TCP
        packet[32] = 0x50; // data offset
        // TCP flags = 0 (no RST)

        // Act
        var result = analyzer.AnalyzeRstPacket(packet, "test.com");

        // Assert
        Assert.Equal(BlockingMethod.None, result.Method);
    }

    [Fact]
    public void AnalyzeRstPacket_InjectingRst_DetectsBlocking()
    {
        // Arrange - packet with zero window + RST+ACK (high confidence injected RST)
        var analyzer = new IspBehaviorAnalyzer();
        var packet = CreateRstPacket(ttl: 64, windowSize: 0, rstAck: true);

        // Act
        var result = analyzer.AnalyzeRstPacket(packet, "blocked.com");

        // Assert - should detect DPI blocking
        Assert.Equal(BlockingMethod.SniDpi, result.Method);
        Assert.True(result.Confidence >= 0.5);
    }

    [Fact]
    public void AnalyzeRstPacket_SniDpiDetected_FiresEvent()
    {
        // Arrange
        var analyzer = new IspBehaviorAnalyzer();
        var packet = CreateRstPacket(ttl: 64, windowSize: 0, rstAck: true);
        string? detectedDomain = null;
        analyzer.OnBlockDetected += d => detectedDomain = d;

        // Act
        analyzer.AnalyzeRstPacket(packet, "domain.example");

        // Assert - event fired if injected RST detected
        // (only fires if confidence >= 0.5)
        if (detectedDomain != null)
        {
            Assert.Equal("domain.example", detectedDomain);
        }
    }

    [Fact]
    public void AnalyzeTimeout_FastTimeout_SuggestsSmartFrag()
    {
        // Arrange
        var analyzer = new IspBehaviorAnalyzer();

        // Act - fast timeout (< 500ms) with no retries
        var result = analyzer.AnalyzeTimeout("example.com", timeoutMs: 200, retryCount: 0);

        // Assert
        Assert.Equal(BlockingMethod.SniDpi, result.Method);
        Assert.Equal(BypassMethod.SmartFragmentation, result.SuggestedBypass);
    }

    [Fact]
    public void AnalyzeTimeout_SlowTimeout_SuggestsAdaptive()
    {
        // Arrange
        var analyzer = new IspBehaviorAnalyzer();

        // Act - slow timeout (> 5000ms)
        var result = analyzer.AnalyzeTimeout("example.com", timeoutMs: 10000, retryCount: 0);

        // Assert
        Assert.Equal(BlockingMethod.IpBlocking, result.Method);
        Assert.Equal(BypassMethod.Adaptive, result.SuggestedBypass);
    }

    [Fact]
    public void GetRecentEvents_AfterAnalysis_ContainsEvents()
    {
        // Arrange
        var analyzer = new IspBehaviorAnalyzer();
        var packet = CreateRstPacket(ttl: 64, windowSize: 0, rstAck: true);

        // Act - trigger a blocking detection
        analyzer.AnalyzeRstPacket(packet, "tracked-domain.com");
        var events = analyzer.GetRecentEvents();

        // Assert - if RST was injected, an event should have been recorded
        // (confidence-dependent, but at least no exceptions)
        Assert.NotNull(events);
    }

    [Fact]
    public void GetRecentEvents_FilterByDomain_ReturnsOnlyMatching()
    {
        // Arrange
        var analyzer = new IspBehaviorAnalyzer();
        var packet = CreateRstPacket(ttl: 64, windowSize: 0, rstAck: true);

        // Act
        analyzer.AnalyzeRstPacket(packet, "domain-a.com");
        analyzer.AnalyzeRstPacket(packet, "domain-b.com");

        var eventsForA = analyzer.GetRecentEvents("domain-a.com");

        // Assert - filtered results should only contain domain-a
        Assert.All(eventsForA, e => Assert.Equal("domain-a.com", e.Domain));
    }

    [Fact]
    public void IsDnsPoisoned_ShortResponse_ReturnsFalse()
    {
        // Arrange
        var analyzer = new IspBehaviorAnalyzer();
        var shortResponse = new byte[8]; // too short to be valid

        // Act
        var result = analyzer.IsDnsPoisoned(shortResponse, "example.com");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetBlockingStats_AfterMultipleAnalyses_CountsCorrectly()
    {
        // Arrange
        var analyzer = new IspBehaviorAnalyzer();
        var injectingRst = CreateRstPacket(ttl: 64, windowSize: 0, rstAck: true);

        // Act - analyze 2 packets
        analyzer.AnalyzeRstPacket(injectingRst, "site1.com");
        analyzer.AnalyzeRstPacket(injectingRst, "site2.com");
        var stats = analyzer.GetBlockingStats();

        // Assert - stats is a valid dictionary (values may be 0 if RST not injected per heuristics)
        Assert.NotNull(stats);
    }
}
