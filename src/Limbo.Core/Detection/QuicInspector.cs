using System.Buffers.Binary;
using System.Text;

namespace Limbo.Core.Detection;

/// <summary>
/// QUIC packet inspector - parses Initial packets and extracts SNI
/// </summary>
public static class QuicInspector
{
    // QUIC version constants
    private const uint QuicV1 = 0x00000001;
    private const uint QuicV2 = 0x6b3343cf;

    /// <summary>
    /// Check if packet is a QUIC Initial packet
    /// </summary>
    public static bool IsQuicInitial(ReadOnlySpan<byte> packet)
    {
        // Minimum size for QUIC Initial with IP/UDP headers
        if (packet.Length < 50) return false;

        // Skip IP header (assume IPv4 for now, 20 bytes min)
        var ipHeaderLen = (packet[0] & 0x0F) * 4;
        if (packet.Length < ipHeaderLen + 8 + 20) return false;

        var udpStart = ipHeaderLen;
        
        // Check if UDP (protocol 17)
        if (packet[9] != 17) return false;

        // Check destination port 443 (QUIC)
        var dstPort = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(udpStart + 2, 2));
        if (dstPort != 443) return false;

        var quicStart = udpStart + 8; // UDP header is 8 bytes
        if (packet.Length <= quicStart) return false;

        return IsQuicInitialPacket(packet[quicStart..]);
    }

    /// <summary>
    /// Check if raw QUIC data is an Initial packet
    /// </summary>
    public static bool IsQuicInitialPacket(ReadOnlySpan<byte> quicData)
    {
        if (quicData.Length < 20) return false;

        var firstByte = quicData[0];

        // Long header format: first bit = 1
        if ((firstByte & 0x80) == 0) return false;

        // Fixed bit should be 1
        if ((firstByte & 0x40) == 0) return false;

        // Long packet type: 00 = Initial
        var packetType = (firstByte >> 4) & 0x03;
        if (packetType != 0x00) return false;

        // Check version (must be valid QUIC version)
        var version = BinaryPrimitives.ReadUInt32BigEndian(quicData.Slice(1, 4));
        if (version != QuicV1 && version != QuicV2 && !IsValidDraftVersion(version))
            return false;

        return true;
    }

    /// <summary>
    /// Extract SNI from QUIC Initial packet
    /// </summary>
    public static string? ExtractSni(ReadOnlySpan<byte> packet)
    {
        try
        {
            // Skip IP header
            var ipHeaderLen = (packet[0] & 0x0F) * 4;
            var udpStart = ipHeaderLen;
            var quicStart = udpStart + 8;

            if (packet.Length <= quicStart + 20) return null;

            var quicData = packet[quicStart..];
            return ExtractSniFromQuic(quicData);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract SNI from raw QUIC Initial data
    /// </summary>
    private static string? ExtractSniFromQuic(ReadOnlySpan<byte> quicData)
    {
        // Parse QUIC Long Header
        // Byte 0: Header form + type
        // Bytes 1-4: Version
        // Byte 5: DCID Length
        // Bytes 6+: DCID
        // Next: SCID Length, SCID, Token, Length, Packet Number, Payload

        var offset = 5;
        
        // DCID
        var dcidLen = quicData[offset];
        offset += 1 + dcidLen;

        if (offset >= quicData.Length) return null;

        // SCID
        var scidLen = quicData[offset];
        offset += 1 + scidLen;

        if (offset >= quicData.Length) return null;

        // Token (varint length)
        var (tokenLen, tokenLenBytes) = ReadVarint(quicData[offset..]);
        offset += tokenLenBytes + (int)tokenLen;

        if (offset >= quicData.Length) return null;

        // Length (varint)
        var (payloadLen, payloadLenBytes) = ReadVarint(quicData[offset..]);
        offset += payloadLenBytes;

        // Packet Number (1-4 bytes based on header)
        var pnLen = (quicData[0] & 0x03) + 1;
        offset += pnLen;

        if (offset >= quicData.Length) return null;

        // Payload is encrypted with INITIAL_SECRET
        // For now, search for SNI pattern in payload (works for unprotected testing)
        var payload = quicData[offset..];
        return SearchForSniPattern(payload);
    }

    /// <summary>
    /// Search for SNI extension pattern in data
    /// </summary>
    private static string? SearchForSniPattern(ReadOnlySpan<byte> data)
    {
        // SNI extension: 0x00 0x00 (type) followed by length and hostname
        for (int i = 0; i < data.Length - 10; i++)
        {
            // Look for SNI extension type (0x00 0x00)
            if (data[i] == 0x00 && data[i + 1] == 0x00)
            {
                var extLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 2, 2));
                if (extLen > 0 && extLen < 256 && i + 4 + extLen <= data.Length)
                {
                    // SNI list length
                    var listLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 4, 2));
                    if (listLen > 0 && listLen < 256)
                    {
                        // Name type (should be 0 for hostname)
                        if (data[i + 6] == 0x00)
                        {
                            var nameLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 7, 2));
                            if (nameLen > 0 && nameLen < 256 && i + 9 + nameLen <= data.Length)
                            {
                                var hostname = Encoding.ASCII.GetString(data.Slice(i + 9, nameLen));
                                if (IsValidHostname(hostname))
                                    return hostname;
                            }
                        }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Read QUIC variable-length integer
    /// </summary>
    private static (ulong Value, int BytesRead) ReadVarint(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return (0, 0);

        var first = data[0];
        var prefix = (first >> 6) & 0x03;

        return prefix switch
        {
            0 => (first & 0x3FUL, 1),
            1 => (BinaryPrimitives.ReadUInt16BigEndian(data) & 0x3FFFUL, 2),
            2 => (BinaryPrimitives.ReadUInt32BigEndian(data) & 0x3FFFFFFFUL, 4),
            3 => (BinaryPrimitives.ReadUInt64BigEndian(data) & 0x3FFFFFFFFFFFFFFFUL, 8),
            _ => (0, 0)
        };
    }

    /// <summary>
    /// Check if version is a valid QUIC draft version
    /// </summary>
    private static bool IsValidDraftVersion(uint version)
    {
        // Draft versions: 0xff0000XX
        return (version & 0xffffff00) == 0xff000000;
    }

    /// <summary>
    /// Validate hostname format
    /// </summary>
    private static bool IsValidHostname(string hostname)
    {
        if (string.IsNullOrEmpty(hostname) || hostname.Length > 253)
            return false;

        foreach (var c in hostname)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-')
                return false;
        }

        return hostname.Contains('.');
    }

    /// <summary>
    /// Get QUIC packet info from raw packet
    /// </summary>
    public static QuicPacketInfo? GetPacketInfo(ReadOnlySpan<byte> packet)
    {
        try
        {
            var ipHeaderLen = (packet[0] & 0x0F) * 4;
            var udpStart = ipHeaderLen;
            var quicStart = udpStart + 8;

            if (packet.Length <= quicStart + 5) return null;

            var quicData = packet[quicStart..];
            var version = BinaryPrimitives.ReadUInt32BigEndian(quicData.Slice(1, 4));

            return new QuicPacketInfo
            {
                Version = version,
                IsInitial = IsQuicInitialPacket(quicData),
                PayloadOffset = quicStart,
                Sni = ExtractSniFromQuic(quicData)
            };
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// QUIC packet information
/// </summary>
public class QuicPacketInfo
{
    public uint Version { get; init; }
    public bool IsInitial { get; init; }
    public int PayloadOffset { get; init; }
    public string? Sni { get; init; }
}
