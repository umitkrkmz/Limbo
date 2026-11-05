using System.Buffers.Binary;

namespace Limbo.Core.Bypass;

/// <summary>
/// Smart fragmentation strategy - splits at exact SNI offset for minimum fragments
/// More effective than basic TCP fragmentation with fewer packets
/// </summary>
public class SmartFragmentationStrategy : IDpiBypassStrategy
{
    public string Name => "Smart Fragmentation";
    public int Priority => 90;
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    private readonly int _splitOffset;

    public SmartFragmentationStrategy(int splitOffset = 3)
    {
        _splitOffset = splitOffset;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsTcp || !context.IsOutbound || !context.IsClientHello)
            return [PacketResult.FromOriginal(packet)];

        var ipHeaderLen = GetIpHeaderLength(packet, context.IsIPv6);
        var tcpHeaderLen = GetTcpHeaderLength(packet, ipHeaderLen);
        var headerLen = ipHeaderLen + tcpHeaderLen;
        var payloadLen = packet.Length - headerLen;

        if (payloadLen < 50) // Too small for TLS ClientHello
            return [PacketResult.FromOriginal(packet)];

        var payload = packet[headerLen..];
        
        // Find SNI offset in TLS payload
        var sniOffset = FindSniOffset(payload);
        if (sniOffset < 0)
            return [PacketResult.FromOriginal(packet)];

        // Split at SNI offset + splitOffset (typically splits "discord" -> "dis" + "cord")
        var splitPoint = sniOffset + _splitOffset;
        if (splitPoint >= payloadLen || splitPoint < 10)
            return [PacketResult.FromOriginal(packet)];

        var header = packet[..headerLen];
        var seqNum = GetTcpSeqNum(packet, ipHeaderLen);

        // Create two fragments
        var results = new List<PacketResult>(2);

        // First fragment: headers + payload until split point
        var firstChunkSize = splitPoint;
        var firstPacket = new byte[headerLen + firstChunkSize];
        header.CopyTo(firstPacket);
        payload[..firstChunkSize].CopyTo(firstPacket.AsSpan(headerLen));
        SetIpTotalLength(firstPacket, context.IsIPv6, (ushort)firstPacket.Length);
        ClearTcpPshFlag(firstPacket, ipHeaderLen);
        results.Add(PacketResult.Modified(firstPacket, recalcChecksum: true));

        // Second fragment: headers + remaining payload
        var secondChunkSize = payloadLen - splitPoint;
        var secondPacket = new byte[headerLen + secondChunkSize];
        header.CopyTo(secondPacket);
        payload[splitPoint..].CopyTo(secondPacket.AsSpan(headerLen));
        SetIpTotalLength(secondPacket, context.IsIPv6, (ushort)secondPacket.Length);
        SetTcpSeqNum(secondPacket, ipHeaderLen, seqNum + (uint)splitPoint);
        results.Add(PacketResult.Modified(secondPacket, recalcChecksum: true));

        return results;
    }

    private static int FindSniOffset(ReadOnlySpan<byte> payload) =>
        Limbo.Core.Detection.TlsClientHello.TryFindServerName(payload, out var hostOffset, out _) ? hostOffset : -1;

    private static int GetIpHeaderLength(ReadOnlySpan<byte> packet, bool isIPv6) 
        => isIPv6 ? 40 : (packet[0] & 0x0F) * 4;

    private static int GetTcpHeaderLength(ReadOnlySpan<byte> packet, int ipHeaderLen)
        => ((packet[ipHeaderLen + 12] >> 4) & 0x0F) * 4;

    private static uint GetTcpSeqNum(ReadOnlySpan<byte> packet, int ipHeaderLen)
        => BinaryPrimitives.ReadUInt32BigEndian(packet[(ipHeaderLen + 4)..]);

    private static void SetTcpSeqNum(Span<byte> packet, int ipHeaderLen, uint seqNum)
        => BinaryPrimitives.WriteUInt32BigEndian(packet[(ipHeaderLen + 4)..], seqNum);

    private static void SetIpTotalLength(Span<byte> packet, bool isIPv6, ushort length)
    {
        if (isIPv6)
            BinaryPrimitives.WriteUInt16BigEndian(packet[4..], (ushort)(length - 40));
        else
            BinaryPrimitives.WriteUInt16BigEndian(packet[2..], length);
    }

    private static void ClearTcpPshFlag(Span<byte> packet, int ipHeaderLen)
        => packet[ipHeaderLen + 13] &= 0xF7;
}
