using Limbo.Core.Detection;

namespace Limbo.Core.Bypass;

/// <summary>
/// QUIC bypass strategy using fake datagram injection
/// Sends a fake UDP packet before the real QUIC Initial to desync DPI
/// </summary>
public class QuicBypassStrategy : IDpiBypassStrategy
{
    public string Name => "QUIC Fake Datagram";
    public int Priority => 150;
    public bool SupportsTcp => false;
    public bool SupportsUdp => true;

    private readonly int _fakePacketSize;

    public QuicBypassStrategy(int fakePacketSize = 64)
    {
        _fakePacketSize = fakePacketSize;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        // Only process UDP traffic on port 443 (QUIC)
        if (!context.IsUdp || context.DstPort != 443)
        {
            return [PacketResult.FromOriginal(packet)];
        }

        // Check if this is a QUIC Initial packet
        if (!QuicInspector.IsQuicInitial(packet))
        {
            return [PacketResult.FromOriginal(packet)];
        }

        // Get packet info to see if we have a target domain
        var info = QuicInspector.GetPacketInfo(packet);
        if (info?.Sni == null)
        {
            return [PacketResult.FromOriginal(packet)];
        }

        // Build fake packet and return both
        var fakePacket = BuildFakeQuicPacket(packet);
        
        return [
            // Send fake packet first (with tiny delay)
            PacketResult.Modified(fakePacket, recalcChecksum: true, delayMs: 0),
            // Then send real packet
            PacketResult.FromOriginal(packet)
        ];
    }

    /// <summary>
    /// Build a fake QUIC-like packet to confuse DPI
    /// </summary>
    private byte[] BuildFakeQuicPacket(ReadOnlySpan<byte> originalPacket)
    {
        // Copy the original packet
        var packet = originalPacket.ToArray();

        // Find QUIC payload offset
        var ipHeaderLen = (packet[0] & 0x0F) * 4;
        var quicStart = ipHeaderLen + 8; // Skip IP + UDP headers

        if (quicStart + 10 > packet.Length) return packet;

        // Corrupt the QUIC header to make it invalid
        // Option 1: Set invalid version
        packet[quicStart + 1] = 0x00;
        packet[quicStart + 2] = 0x00;
        packet[quicStart + 3] = 0x00;
        packet[quicStart + 4] = 0x00;

        // Option 2: Corrupt packet type (set reserved bits)
        packet[quicStart] = (byte)(packet[quicStart] | 0x08);

        // Add random garbage to payload to desync DPI state
        var rand = new Random();
        for (var i = quicStart + 10; i < Math.Min(quicStart + _fakePacketSize, packet.Length); i++)
        {
            packet[i] = (byte)rand.Next(256);
        }

        // Set a low TTL so packet doesn't reach server (optional)
        // This way only DPI sees it
        packet[8] = 3; // TTL = 3 hops

        return packet;
    }
}
