using System.Buffers.Binary;
using System.Net;
using Limbo.Core.Dns;
using static Limbo.Tests.DnsTestPackets;

namespace Limbo.Tests;

public class DnsPacketTests
{
    [Fact]
    public void TryParseQuery_IPv4Query()
    {
        Assert.True(DnsPacket.TryParseQuery(QueryV4("discord.com"), out var offset, out var name));
        Assert.Equal(28, offset);
        Assert.Equal("discord.com", name);
    }

    [Fact]
    public void TryParseQuery_IPv6QueryToRouter()
    {
        Assert.True(DnsPacket.TryParseQuery(QueryV6("gateway.discord.gg"), out var offset, out var name));
        Assert.Equal(48, offset);
        Assert.Equal("gateway.discord.gg", name);
    }

    [Fact]
    public void TryParseQuery_RejectsNonDns()
    {
        var https = Udp(ClientV4, RouterV4, ClientPort, 443, DnsQuery("discord.com"));
        Assert.False(DnsPacket.TryParseQuery(https, out _, out _));

        var response = DnsQuery("discord.com");
        response[2] |= 0x80; // QR = response
        Assert.False(DnsPacket.TryParseQuery(Udp(ClientV4, RouterV4, ClientPort, 53, response), out _, out _));

        var tooShort = Udp(ClientV4, RouterV4, ClientPort, 53, [1, 2, 3]);
        Assert.False(DnsPacket.TryParseQuery(tooShort, out _, out _));

        var tcp = QueryV4("discord.com");
        tcp[9] = 6;
        Assert.False(DnsPacket.TryParseQuery(tcp, out _, out _));
    }

    [Fact]
    public void BuildResponse_IPv4_SwapsEndpointsAndFixesLengths()
    {
        var query = QueryV4("discord.com");
        var dnsResponse = FakeResponse(id: 0); // DoH servers may answer with ID 0

        var reply = DnsPacket.BuildResponse(query, dnsResponse);

        Assert.Equal(0x45, reply[0]);
        Assert.Equal(reply.Length, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2)));
        Assert.Equal(17, reply[9]);
        Assert.Equal(RouterV4, new IPAddress(reply.AsSpan(12, 4)));
        Assert.Equal(ClientV4, new IPAddress(reply.AsSpan(16, 4)));
        Assert.Equal(53, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(20)));
        Assert.Equal(ClientPort, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(22)));
        Assert.Equal(8 + dnsResponse.Length, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(24)));
        Assert.Equal(QueryId, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(28)));
        Assert.Equal(dnsResponse.AsSpan(2).ToArray(), reply.AsSpan(30).ToArray());
    }

    [Fact]
    public void BuildResponse_IPv6_SwapsEndpointsAndFixesLengths()
    {
        var query = QueryV6("discord.com");
        var dnsResponse = FakeResponse(id: QueryId);

        var reply = DnsPacket.BuildResponse(query, dnsResponse);

        Assert.Equal(6, reply[0] >> 4);
        Assert.Equal(8 + dnsResponse.Length, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(4)));
        Assert.Equal(17, reply[6]);
        Assert.Equal(RouterV6, new IPAddress(reply.AsSpan(8, 16)));
        Assert.Equal(ClientV6, new IPAddress(reply.AsSpan(24, 16)));
        Assert.Equal(53, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(40)));
        Assert.Equal(ClientPort, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(42)));
        Assert.Equal(dnsResponse, reply.AsSpan(48).ToArray());
    }

    [Fact]
    public void DnsMessage_BuildQuery_IsAParsableStandardQuery()
    {
        var query = DnsMessage.BuildQuery("discord.com.", DnsMessage.TypeAaaa);

        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(query)); // ID 0 (RFC 8484)
        Assert.True(DnsPacket.TryParseQuery(Udp(ClientV4, RouterV4, ClientPort, 53, query), out _, out var name));
        Assert.Equal("discord.com", name);
        Assert.Equal(DnsMessage.TypeAaaa, BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(query.Length - 4)));
    }

    [Fact]
    public void DnsMessage_ReadAddresses_FollowsCompressedNames()
    {
        var (addresses, ttl) = DnsMessage.ReadAddresses(FakeResponse(id: 1), DnsMessage.TypeA);

        Assert.Equal(IPAddress.Parse("162.159.128.233"), Assert.Single(addresses));
        Assert.Equal(60, ttl);
        Assert.Empty(DnsMessage.ReadAddresses(FakeResponse(id: 1), DnsMessage.TypeAaaa).Addresses);
    }

    [Fact]
    public void DnsMessage_ReadAddresses_ToleratesGarbage()
    {
        Assert.Empty(DnsMessage.ReadAddresses([1, 2, 3], DnsMessage.TypeA).Addresses);
        var truncated = FakeResponse(id: 1)[..^3];
        Assert.Empty(DnsMessage.ReadAddresses(truncated, DnsMessage.TypeA).Addresses);
        var servfail = FakeResponse(id: 1);
        servfail[3] |= 2;
        Assert.Empty(DnsMessage.ReadAddresses(servfail, DnsMessage.TypeA).Addresses);
    }

    private static byte[] FakeResponse(ushort id)
    {
        var response = DnsQuery("discord.com", id);
        response[2] |= 0x80; // QR
        response[7] = 1;     // ANCOUNT
        return [.. response, 0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 162, 159, 128, 233];
    }
}
