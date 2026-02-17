namespace Limbo.Core.Bypass;

/// <summary>
/// UDP Fake strategy - applies fake packet techniques for UDP traffic (QUIC/voice)
/// </summary>
public class UdpFakeStrategy : IDpiBypassStrategy
{
    private readonly int _fakeTtl;

    public string Name => "UDP Fake";
    public int Priority => 60;
    public bool SupportsTcp => false;
    public bool SupportsUdp => true;

    public UdpFakeStrategy(int fakeTtl = 1)
    {
        _fakeTtl = Math.Clamp(fakeTtl, 1, 10);
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsUdp || !context.IsOutbound)
            return [PacketResult.FromOriginal(packet)];

        var ipHeaderLen = context.IsIPv6 ? 40 : (packet[0] & 0x0F) * 4;
        
        if (packet.Length < ipHeaderLen + 8)
            return [PacketResult.FromOriginal(packet)];

        var results = new List<PacketResult>(2);

        // 1. Send fake UDP packet with corrupted payload
        var fakePacket = packet.ToArray();
        
        // Corrupt part of the payload (after UDP header)
        var udpPayloadStart = ipHeaderLen + 8;
        if (fakePacket.Length > udpPayloadStart + 4)
        {
            // Flip some bits in the payload to make it invalid
            fakePacket[udpPayloadStart] ^= 0xFF;
            fakePacket[udpPayloadStart + 1] ^= 0xFF;
        }
        
        // Set low TTL so it dies before server
        if (context.IsIPv6)
            fakePacket[7] = (byte)_fakeTtl; // Hop Limit
        else
            fakePacket[8] = (byte)_fakeTtl; // TTL
        
        results.Add(PacketResult.Modified(fakePacket, recalcChecksum: true, delayMs: 0));
        
        // 2. Send real packet
        results.Add(PacketResult.FromOriginal(packet));
        
        return results;
    }
}
