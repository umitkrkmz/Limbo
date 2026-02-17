using System.Buffers.Binary;

namespace Limbo.Core.Bypass;

/// <summary>
/// Fake TTL strategy - sends decoy packets with low TTL that reach DPI but expire before server
/// </summary>
public class FakeTtlStrategy : IDpiBypassStrategy
{
    public string Name => "Fake TTL";
    public int Priority => 70;
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    private readonly byte _fakeTtl;

    public FakeTtlStrategy(byte fakeTtl = 1)
    {
        _fakeTtl = fakeTtl;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsTcp || !context.IsOutbound || !context.IsClientHello)
            return [PacketResult.FromOriginal(packet)];

        if (context.IsIPv6)
        {
            // IPv6 uses Hop Limit instead of TTL, same concept
            return ProcessIPv6(packet);
        }

        return ProcessIPv4(packet);
    }

    private IReadOnlyList<PacketResult> ProcessIPv4(ReadOnlySpan<byte> packet)
    {
        var results = new List<PacketResult>(2);
        
        // 1. Create fake packet with low TTL
        // This packet will be seen by DPI (which is closer) but will expire before reaching the real server
        var fakePacket = packet.ToArray();
        
        // Save original TTL
        var originalTtl = fakePacket[8];
        
        // Set very low TTL (will expire after DPI but before server)
        fakePacket[8] = _fakeTtl;
        
        // Corrupt the fake packet slightly so server ignores it if it somehow gets through
        // We can set an invalid TCP checksum by not recalculating it
        // Or send garbage in payload - DPI will see "valid" looking packet going to allowed destination
        
        // Send fake packet first (DPI thinks it's the real one)
        results.Add(PacketResult.Modified(fakePacket, recalcChecksum: true, delayMs: 0));
        
        // 2. Send real packet slightly delayed
        // DPI already saw a packet for this stream, may not inspect this one as thoroughly
        var realPacket = packet.ToArray();
        results.Add(PacketResult.Modified(realPacket, recalcChecksum: false, delayMs: 5));
        
        return results;
    }

    private IReadOnlyList<PacketResult> ProcessIPv6(ReadOnlySpan<byte> packet)
    {
        var results = new List<PacketResult>(2);
        
        // 1. Create fake packet with low Hop Limit
        var fakePacket = packet.ToArray();
        
        // IPv6 Hop Limit is at byte 7
        fakePacket[7] = _fakeTtl;
        
        results.Add(PacketResult.Modified(fakePacket, recalcChecksum: true, delayMs: 0));
        
        // 2. Send real packet
        var realPacket = packet.ToArray();
        results.Add(PacketResult.Modified(realPacket, recalcChecksum: false, delayMs: 5));
        
        return results;
    }
}
