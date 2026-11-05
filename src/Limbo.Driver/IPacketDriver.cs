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
    /// Metadata of the most recently received packet; overwritten by the next Receive.
    /// </summary>
    PacketAddress LastAddress { get; }

    /// <summary>
    /// Send using a previously captured address instead of the latest one (checksums
    /// are always recalculated). With <paramref name="asInboundReply"/>, the packet is
    /// injected towards this machine as if it arrived on the interface the captured
    /// packet left through - i.e. a reply to that outbound packet.
    /// </summary>
    bool Send(ReadOnlySpan<byte> packet, PacketAddress address, bool asInboundReply);

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
