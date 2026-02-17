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

    /// <summary>
    /// Find the offset of SNI extension value in TLS payload
    /// </summary>
    private static int FindSniOffset(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 43)
            return -1;

        try
        {
            // Skip TLS record header (5 bytes) + handshake header (4 bytes)
            var offset = 9;
            
            // Skip client version (2) + random (32)
            offset += 34;
            
            if (offset >= payload.Length)
                return -1;
            
            // Skip session ID
            var sessionIdLen = payload[offset];
            offset += 1 + sessionIdLen;
            
            if (offset + 2 >= payload.Length)
                return -1;
            
            // Skip cipher suites
            var cipherSuitesLen = (payload[offset] << 8) | payload[offset + 1];
            offset += 2 + cipherSuitesLen;
            
            if (offset >= payload.Length)
                return -1;
            
            // Skip compression methods
            var compressionLen = payload[offset];
            offset += 1 + compressionLen;
            
            if (offset + 2 >= payload.Length)
                return -1;
            
            // Extensions length
            var extensionsLen = (payload[offset] << 8) | payload[offset + 1];
            offset += 2;
            
            var extensionsEnd = offset + extensionsLen;
            
            while (offset + 4 < extensionsEnd && offset + 4 < payload.Length)
            {
                var extType = (payload[offset] << 8) | payload[offset + 1];
                var extLen = (payload[offset + 2] << 8) | payload[offset + 3];
                offset += 4;
                
                // SNI extension type = 0x0000
                if (extType == 0 && offset + 5 <= payload.Length)
                {
                    // Return offset of the actual hostname (after SNI list length + type + name length)
                    return offset + 5;
                }
                
                offset += extLen;
            }
        }
        catch
        {
            // Malformed packet
        }
        
        return -1;
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
