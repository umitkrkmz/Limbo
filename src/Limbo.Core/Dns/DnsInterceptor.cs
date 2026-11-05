using Limbo.Driver;

namespace Limbo.Core.Dns;

/// <summary>
/// Answers this machine's plain DNS queries (UDP 53, IPv4 and IPv6, any app) over
/// DNS-over-HTTPS instead of letting them reach the network, where ISPs poison them
/// (e.g. discord.com -> a block-page IP) - typically via a router's IPv6 DNS that the
/// user's encrypted IPv4 DNS setting never covered. System DNS settings are never
/// touched; stopping the engine restores normal behavior.
///
/// A query is dropped from the capture loop and answered asynchronously, so a DoH
/// round trip never stalls other traffic. If DoH fails, the original query is sent
/// on unchanged: DNS keeps working (unprotected) instead of breaking.
/// </summary>
public sealed class DnsInterceptor : IDisposable
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]?>> _query;
    private readonly string? _passThroughName;
    private readonly DoHResolver? _ownedResolver;
    private readonly CancellationTokenSource _cts = new();
    private long _answered;
    private long _fallbacks;

    public event Action<string>? OnLog;

    public DnsInterceptor(string dohServerUrl)
        : this(null!, PassThroughNameFor(dohServerUrl))
    {
        _ownedResolver = new DoHResolver(dohServerUrl);
        _query = _ownedResolver.QueryWireAsync;
    }

    internal DnsInterceptor(Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]?>> query, string? passThroughName)
    {
        _query = query;
        _passThroughName = passThroughName;
    }

    public long AnsweredViaDoH => Interlocked.Read(ref _answered);
    public long FellBackToPlainDns => Interlocked.Read(ref _fallbacks);

    /// <summary>
    /// Takes over <paramref name="packet"/> if it's an outbound DNS query. When this
    /// returns true the caller must NOT reinject the packet - it will be answered (or
    /// forwarded on DoH failure) through <paramref name="driver"/> using <paramref name="address"/>.
    /// </summary>
    public bool TryIntercept(ReadOnlySpan<byte> packet, PacketAddress address, IPacketDriver driver)
    {
        if (!address.IsOutbound) return false;
        if (!DnsPacket.TryParseQuery(packet, out var dnsOffset, out var queryName)) return false;

        // The DoH server's own hostname has to resolve normally, or the resolver
        // would wait on itself.
        if (_passThroughName != null && string.Equals(queryName, _passThroughName, StringComparison.OrdinalIgnoreCase))
            return false;

        _ = AnswerAsync(packet.ToArray(), dnsOffset, queryName!, address, driver);
        return true;
    }

    internal async Task AnswerAsync(byte[] queryPacket, int dnsOffset, string queryName, PacketAddress address, IPacketDriver driver)
    {
        byte[]? response = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(QueryTimeout);
            response = await _query(queryPacket.AsMemory(dnsOffset), timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or ObjectDisposedException)
        {
        }

        // A UDP length field can't describe more; a real DNS response never gets close.
        const int maxDnsPayload = ushort.MaxValue - 48;
        if (response != null && response.Length <= maxDnsPayload)
        {
            driver.Send(DnsPacket.BuildResponse(queryPacket, response), address, asInboundReply: true);
            Interlocked.Increment(ref _answered);
            return;
        }

        driver.Send(queryPacket, address, asInboundReply: false);
        if (Interlocked.Increment(ref _fallbacks) == 1)
        {
            OnLog?.Invoke($"[DNS] DoH unreachable - sending plain DNS for {queryName} (and later failures) unprotected");
        }
    }

    internal static string? PassThroughNameFor(string dohServerUrl) =>
        Uri.TryCreate(dohServerUrl, UriKind.Absolute, out var uri) && uri.HostNameType == UriHostNameType.Dns
            ? uri.Host
            : null;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _ownedResolver?.Dispose();
    }
}
