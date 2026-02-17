using System.Buffers.Binary;

namespace Limbo.Core.Bypass;

/// <summary>
/// TCP Desync strategy - sends packets out of order to break DPI state machine
/// </summary>
public class DesyncStrategy : IDpiBypassStrategy
{
    public string Name => "TCP Desync";
    public int Priority => 80;
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    private readonly int _splitPosition;

    public DesyncStrategy(int splitPosition = 3)
    {
        _splitPosition = splitPosition;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsTcp || !context.IsOutbound || !context.IsClientHello)
            return [PacketResult.FromOriginal(packet)];

        var ipHeaderLen = GetIpHeaderLength(packet, context.IsIPv6);
        var tcpHeaderLen = GetTcpHeaderLength(packet, ipHeaderLen);
        var headerLen = ipHeaderLen + tcpHeaderLen;
        var payloadLen = packet.Length - headerLen;

        if (payloadLen <= _splitPosition)
            return [PacketResult.FromOriginal(packet)];

        var header = packet[..headerLen];
        var payload = packet[headerLen..];
        var seqNum = GetTcpSeqNum(packet, ipHeaderLen);

        var results = new List<PacketResult>(2);

        // DESYNC: Send second part FIRST (out of order)
        // This breaks DPI state machines that expect sequential data

        // Second fragment (sent first with delay)
        var secondChunkSize = payloadLen - _splitPosition;
        var secondPacket = new byte[headerLen + secondChunkSize];
        header.CopyTo(secondPacket);
        payload[_splitPosition..].CopyTo(secondPacket.AsSpan(headerLen));
        SetIpTotalLength(secondPacket, context.IsIPv6, (ushort)secondPacket.Length);
        SetTcpSeqNum(secondPacket, ipHeaderLen, seqNum + (uint)_splitPosition);
        // Small delay to ensure out-of-order delivery
        results.Add(PacketResult.Modified(secondPacket, recalcChecksum: true, delayMs: 0));

        // First fragment (sent second)
        var firstChunkSize = _splitPosition;
        var firstPacket = new byte[headerLen + firstChunkSize];
        header.CopyTo(firstPacket);
        payload[..firstChunkSize].CopyTo(firstPacket.AsSpan(headerLen));
        SetIpTotalLength(firstPacket, context.IsIPv6, (ushort)firstPacket.Length);
        ClearTcpPshFlag(firstPacket, ipHeaderLen);
        // Delay to send after the "second" packet
        results.Add(PacketResult.Modified(firstPacket, recalcChecksum: true, delayMs: 10));

        return results;
    }

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
