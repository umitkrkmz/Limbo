namespace Limbo.Driver;

/// <summary>
/// Interface for packet capture drivers
/// Abstraction allows swapping WinDivert for WFP or other drivers
/// </summary>
public interface IPacketDriver : IDisposable
{
    /// <summary>
    /// Open a capture handle with the given filter
    /// </summary>
    bool Open(string filter);
    
    /// <summary>
    /// Close the capture handle
    /// </summary>
    void Close();
    
    /// <summary>
    /// Receive a packet
    /// </summary>
    bool Receive(Span<byte> buffer, out int length, out bool isOutbound);
    
    /// <summary>
    /// Send/reinject a packet
    /// </summary>
    bool Send(ReadOnlySpan<byte> packet, bool recalcChecksum);
    
    /// <summary>
    /// Whether the driver is currently open
    /// </summary>
    bool IsOpen { get; }
}

/// <summary>
/// Packet capture statistics
/// </summary>
public readonly struct DriverStats
{
    public long PacketsReceived { get; init; }
    public long PacketsSent { get; init; }
    public long Errors { get; init; }
}
