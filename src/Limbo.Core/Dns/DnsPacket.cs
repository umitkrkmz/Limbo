using System.Buffers.Binary;
using System.Text;

namespace Limbo.Core.Dns;

/// <summary>
/// Raw IPv4/IPv6 + UDP handling for DNS interception: recognizes an outbound DNS
/// query and builds the inbound reply packet around a DNS response. The DNS message
/// itself is never re-encoded - DoH (RFC 8484) carries it in wire format as-is.
/// </summary>
internal static class DnsPacket
{
    private const int UdpHeaderLength = 8;
    private const int DnsHeaderLength = 12;
    private const byte UdpProtocol = 17;
    private const int DnsPort = 53;

    /// <summary>
    /// True for an IPv4/IPv6 UDP packet to port 53 carrying a standard DNS query.
    /// <paramref name="dnsOffset"/> is where the DNS message starts.
    /// </summary>
    public static bool TryParseQuery(ReadOnlySpan<byte> packet, out int dnsOffset, out string? queryName)
    {
        dnsOffset = 0;
        queryName = null;

        if (!TryGetUdpOffset(packet, out var udpOffset)) return false;
        if (BinaryPrimitives.ReadUInt16BigEndian(packet[(udpOffset + 2)..]) != DnsPort) return false;

        dnsOffset = udpOffset + UdpHeaderLength;
        var dns = packet[dnsOffset..];
        if (dns.Length < DnsHeaderLength) return false;

        var isResponse = (dns[2] & 0x80) != 0;
        var opcode = (dns[2] >> 3) & 0x0F;
        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(dns[4..]);
        if (isResponse || opcode != 0 || questionCount == 0) return false;

        queryName = ReadName(dns[DnsHeaderLength..]);
        return queryName != null;
    }

    /// <summary>
    /// Builds the packet a DNS server would have sent back for <paramref name="queryPacket"/>:
    /// addresses and ports swapped, lengths fixed up, <paramref name="dnsResponse"/> as the
    /// payload with the query's transaction ID. Checksums are left zero for the driver to fill in.
    /// </summary>
    public static byte[] BuildResponse(ReadOnlySpan<byte> queryPacket, ReadOnlySpan<byte> dnsResponse)
    {
        var isIPv6 = queryPacket[0] >> 4 == 6;
        var ipHeaderLength = isIPv6 ? 40 : 20; // IPv4 options of the query aren't carried over
        var udpLength = UdpHeaderLength + dnsResponse.Length;
        var reply = new byte[ipHeaderLength + udpLength];

        if (isIPv6)
        {
            queryPacket[..4].CopyTo(reply);                                       // version, class, flow label
            BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(4), (ushort)udpLength);
            reply[6] = UdpProtocol;
            reply[7] = 64;                                                        // hop limit
            queryPacket.Slice(24, 16).CopyTo(reply.AsSpan(8));                    // src = query's dst
            queryPacket.Slice(8, 16).CopyTo(reply.AsSpan(24));                    // dst = query's src
        }
        else
        {
            reply[0] = 0x45;                                                      // IPv4, 20-byte header
            reply[1] = queryPacket[1];
            BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), (ushort)reply.Length);
            queryPacket.Slice(4, 2).CopyTo(reply.AsSpan(4));                      // identification
            reply[6] = 0x40;                                                      // don't fragment
            reply[8] = 64;                                                        // TTL
            reply[9] = UdpProtocol;
            queryPacket.Slice(16, 4).CopyTo(reply.AsSpan(12));
            queryPacket.Slice(12, 4).CopyTo(reply.AsSpan(16));
        }

        TryGetUdpOffset(queryPacket, out var queryUdpOffset);
        var udp = reply.AsSpan(ipHeaderLength);
        queryPacket.Slice(queryUdpOffset + 2, 2).CopyTo(udp);                     // src port = query's dst (53)
        queryPacket.Slice(queryUdpOffset, 2).CopyTo(udp[2..]);                    // dst port = query's src
        BinaryPrimitives.WriteUInt16BigEndian(udp[4..], (ushort)udpLength);

        var dns = udp[UdpHeaderLength..];
        dnsResponse.CopyTo(dns);
        // DoH servers may answer with ID 0 (RFC 8484 recommends clients send 0); the
        // waiting resolver only accepts its own ID.
        queryPacket.Slice(queryUdpOffset + UdpHeaderLength, 2).CopyTo(dns);

        return reply;
    }

    private static bool TryGetUdpOffset(ReadOnlySpan<byte> packet, out int udpOffset)
    {
        udpOffset = 0;
        if (packet.Length < 20) return false;

        var version = packet[0] >> 4;
        if (version == 4)
        {
            var headerLength = (packet[0] & 0x0F) * 4;
            var isFragment = (BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) & 0x3FFF) != 0;
            if (packet[9] != UdpProtocol || headerLength < 20 || isFragment) return false;
            udpOffset = headerLength;
        }
        else if (version == 6)
        {
            // Only UDP directly after the fixed header; extension headers are left alone.
            if (packet.Length < 40 || packet[6] != UdpProtocol) return false;
            udpOffset = 40;
        }
        else
        {
            return false;
        }

        return packet.Length >= udpOffset + UdpHeaderLength;
    }

    private static string? ReadName(ReadOnlySpan<byte> question)
    {
        var name = new StringBuilder();
        var position = 0;
        while (position < question.Length)
        {
            var labelLength = question[position];
            if (labelLength == 0) return name.Length == 0 ? "." : name.ToString();
            if (labelLength > 63 || position + 1 + labelLength > question.Length) return null;

            if (name.Length > 0) name.Append('.');
            name.Append(Encoding.ASCII.GetString(question.Slice(position + 1, labelLength)));
            position += 1 + labelLength;
        }

        return null;
    }
}
