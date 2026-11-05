using System.Buffers.Binary;
using Limbo.Core.Dns;
using static Limbo.Tests.DnsTestPackets;

namespace Limbo.Tests;

public class DnsInterceptorTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AnswersOutboundQueryViaDoH_AsInboundReply()
    {
        byte[]? sentToDoH = null;
        using var interceptor = new DnsInterceptor((query, _) =>
        {
            sentToDoH = query.ToArray();
            var response = query.ToArray();
            response[2] |= 0x80;
            return Task.FromResult<byte[]?>(response);
        }, passThroughName: null);
        var driver = new RecordingDriver();

        Assert.True(interceptor.TryIntercept(QueryV6("discord.com"), Outbound(), driver));
        await driver.FirstSend.Task.WaitAsync(Wait);

        Assert.Equal(DnsQuery("discord.com"), sentToDoH);
        var (packet, _, asInboundReply) = Assert.Single(driver.Sent);
        Assert.True(asInboundReply);
        Assert.Equal(QueryId, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(48)));
        Assert.Equal(1, interceptor.AnsweredViaDoH);
        Assert.Equal(0, interceptor.FellBackToPlainDns);
    }

    [Fact]
    public async Task DoHFailure_ForwardsOriginalQueryUnchanged()
    {
        using var interceptor = new DnsInterceptor((_, _) => Task.FromResult<byte[]?>(null), passThroughName: null);
        var driver = new RecordingDriver();
        var query = QueryV4("discord.com");

        Assert.True(interceptor.TryIntercept(query, Outbound(), driver));
        await driver.FirstSend.Task.WaitAsync(Wait);

        var (packet, address, asInboundReply) = Assert.Single(driver.Sent);
        Assert.False(asInboundReply);
        Assert.True(address.IsOutbound);
        Assert.Equal(query, packet);
        Assert.Equal(1, interceptor.FellBackToPlainDns);
    }

    [Fact]
    public async Task DoHThrowing_StillForwardsOriginalQuery()
    {
        using var interceptor = new DnsInterceptor((_, _) => throw new HttpRequestException("blocked"), passThroughName: null);
        var driver = new RecordingDriver();

        Assert.True(interceptor.TryIntercept(QueryV4("discord.com"), Outbound(), driver));
        await driver.FirstSend.Task.WaitAsync(Wait);

        Assert.False(Assert.Single(driver.Sent).AsInboundReply);
    }

    [Fact]
    public void LeavesNonQueriesAndTheDoHServersOwnNameAlone()
    {
        using var interceptor = new DnsInterceptor((_, _) => Task.FromResult<byte[]?>(null), passThroughName: "cloudflare-dns.com");
        var driver = new RecordingDriver();

        Assert.False(interceptor.TryIntercept(QueryV4("cloudflare-dns.com"), Outbound(), driver));
        Assert.False(interceptor.TryIntercept(QueryV4("discord.com"), Inbound(), driver));
        Assert.False(interceptor.TryIntercept(Udp(ClientV4, RouterV4, ClientPort, 443, DnsQuery("discord.com")), Outbound(), driver));
        Assert.Empty(driver.Sent);
    }

    [Theory]
    [InlineData("https://cloudflare-dns.com/dns-query", "cloudflare-dns.com")]
    [InlineData("https://dns.google/dns-query", "dns.google")]
    [InlineData("https://1.1.1.1/dns-query", null)]
    [InlineData("https://[2606:4700:4700::1111]/dns-query", null)]
    public void PassThroughName_OnlyForHostnameServers(string url, string? expected)
    {
        Assert.Equal(expected, DnsInterceptor.PassThroughNameFor(url));
    }

    // ResolveAsync used the JSON API, which only Cloudflare serves at /dns-query - Google
    // and Quad9 silently returned nothing, so DNS tests/tamper checks quietly passed.
    [Theory]
    [InlineData("https://1.1.1.1/dns-query")]
    [InlineData("https://dns.google/dns-query")]
    [InlineData("https://dns.quad9.net/dns-query")]
    public async Task ResolveAsync_RealServer_FindsAddressesWithAnyProvider(string server)
    {
        using var resolver = new DoHResolver(server);
        var addresses = await resolver.ResolveAsync("discord.com", useCache: false);
        if (addresses.Length == 0 && !await NetworkReachable()) return;

        Assert.NotEmpty(addresses);
    }

    private static async Task<bool> NetworkReachable()
    {
        using var probe = new DoHResolver("https://1.1.1.1/dns-query");
        return await probe.QueryWireAsync(DnsQuery("example.com")) != null;
    }

    // Real DoH servers; must tolerate no network. Includes Quad9, which rejects HTTP/1.1.
    [Theory]
    [InlineData("https://1.1.1.1/dns-query")]
    [InlineData("https://dns.google/dns-query")]
    [InlineData("https://dns.quad9.net/dns-query")]
    public async Task QueryWireAsync_RealServer_ReturnsAnswerForSameQuestion(string server)
    {
        using var resolver = new DoHResolver(server);
        var response = await resolver.QueryWireAsync(DnsQuery("discord.com"));
        if (response == null) return;

        Assert.Equal(QueryId, BinaryPrimitives.ReadUInt16BigEndian(response));
        Assert.True((response[2] & 0x80) != 0, "QR bit set");
        Assert.Equal(0, response[3] & 0x0F); // NOERROR
        Assert.True(BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(6)) > 0, "has answers");
    }
}
