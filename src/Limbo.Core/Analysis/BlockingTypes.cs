namespace Limbo.Core.Analysis;

/// <summary>
/// ISP blocking detection method
/// </summary>
public enum BlockingMethod
{
    /// <summary>No blocking detected</summary>
    None,
    
    /// <summary>DNS response is poisoned/redirected</summary>
    DnsPoisoning,
    
    /// <summary>DPI reading SNI and sending RST</summary>
    SniDpi,
    
    /// <summary>IP address is blocked</summary>
    IpBlocking,
    
    /// <summary>Connection is throttled/slowed</summary>
    Throttling,
    
    /// <summary>RST packet injection</summary>
    ConnectionReset,
    
    /// <summary>Connection times out (silent drop)</summary>
    TcpTimeout,
    
    /// <summary>Unknown blocking method</summary>
    Unknown
}

/// <summary>
/// Recorded blocking event for analysis
/// </summary>
public record BlockingEvent
{
    /// <summary>When the blocking was detected</summary>
    public required DateTime Timestamp { get; init; }
    
    /// <summary>Target domain</summary>
    public required string Domain { get; init; }
    
    /// <summary>Detected blocking method</summary>
    public required BlockingMethod Method { get; init; }
    
    /// <summary>Target IP address</summary>
    public string? TargetIp { get; init; }
    
    /// <summary>Target port</summary>
    public ushort TargetPort { get; init; }
    
    /// <summary>Connection latency before block (ms)</summary>
    public int LatencyMs { get; init; }
    
    /// <summary>Raw packet data for forensic analysis</summary>
    public byte[]? RawPacket { get; init; }
    
    /// <summary>ISP fingerprint if identifiable</summary>
    public string? IspFingerprint { get; init; }
    
    /// <summary>Additional notes/context</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Result of blocking analysis
/// </summary>
public record AnalysisResult
{
    public required BlockingMethod Method { get; init; }
    public required double Confidence { get; init; }  // 0.0 - 1.0
    public required string Recommendation { get; init; }
    public Bypass.BypassMethod SuggestedBypass { get; init; }
}
