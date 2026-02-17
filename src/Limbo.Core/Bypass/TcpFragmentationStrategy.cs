using System.Buffers.Binary;

namespace Limbo.Core.Bypass;

/// <summary>
/// TCP fragmentation strategy - splits TCP payload into smaller segments
/// This defeats DPI that doesn't reassemble TCP streams
/// Adapted from ZeroDivert
/// </summary>
public class TcpFragmentationStrategy : IDpiBypassStrategy
{
    public string Name => "TCP Fragmentation";
    public int Priority => 100;
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    private readonly int _fragmentSize;

    public TcpFragmentationStrategy(int fragmentSize = 2)
    {
        _fragmentSize = fragmentSize;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsTcp || !context.IsOutbound)
            return [PacketResult.FromOriginal(packet)];

        // Only fragment ClientHello or HTTP requests
        if (!context.IsClientHello && !context.HasHttpHost)
            return [PacketResult.FromOriginal(packet)];

        var ipHeaderLen = GetIpHeaderLength(packet, context.IsIPv6);
        var tcpHeaderLen = GetTcpHeaderLength(packet, ipHeaderLen);
        var headerLen = ipHeaderLen + tcpHeaderLen;
        var payloadLen = packet.Length - headerLen;

        // Need at least some payload to fragment
        if (payloadLen <= _fragmentSize)
            return [PacketResult.FromOriginal(packet)];

        var results = new List<PacketResult>();
        var payload = packet[headerLen..];
        var header = packet[..headerLen];
        var seqNum = GetTcpSeqNum(packet, ipHeaderLen);

        var offset = 0;
        while (offset < payloadLen)
        {
            var chunkSize = Math.Min(_fragmentSize, payloadLen - offset);
            var chunk = payload.Slice(offset, chunkSize);

            var newPacket = new byte[headerLen + chunkSize];
            header.CopyTo(newPacket);
            chunk.CopyTo(newPacket.AsSpan(headerLen));

            // Update IP total length
            SetIpTotalLength(newPacket, context.IsIPv6, (ushort)newPacket.Length);

            // Update TCP sequence number
            SetTcpSeqNum(newPacket, ipHeaderLen, seqNum + (uint)offset);

            // Clear PSH flag for intermediate fragments, set for last
            if (offset + chunkSize < payloadLen)
                ClearTcpPshFlag(newPacket, ipHeaderLen);

            results.Add(PacketResult.Modified(newPacket, recalcChecksum: true));
            offset += chunkSize;
        }

        return results;
    }

    private static int GetIpHeaderLength(ReadOnlySpan<byte> packet, bool isIPv6)
    {
        if (isIPv6) return 40;
        return (packet[0] & 0x0F) * 4;
    }

    private static int GetTcpHeaderLength(ReadOnlySpan<byte> packet, int ipHeaderLen)
    {
        return ((packet[ipHeaderLen + 12] >> 4) & 0x0F) * 4;
    }

    private static uint GetTcpSeqNum(ReadOnlySpan<byte> packet, int ipHeaderLen)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(packet[(ipHeaderLen + 4)..]);
    }

    private static void SetTcpSeqNum(Span<byte> packet, int ipHeaderLen, uint seqNum)
    {
        BinaryPrimitives.WriteUInt32BigEndian(packet[(ipHeaderLen + 4)..], seqNum);
    }

    private static void SetIpTotalLength(Span<byte> packet, bool isIPv6, ushort length)
    {
        if (isIPv6)
        {
            // IPv6 payload length (excludes header)
            BinaryPrimitives.WriteUInt16BigEndian(packet[4..], (ushort)(length - 40));
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(packet[2..], length);
        }
    }

    private static void ClearTcpPshFlag(Span<byte> packet, int ipHeaderLen)
    {
        packet[ipHeaderLen + 13] &= 0xF7; // Clear PSH (0x08)
    }
}
