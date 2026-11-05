using System.Net;
using System.Net.Http.Headers;

namespace Limbo.Core.Dns;

/// <summary>
/// DNS over HTTPS (DoH) resolver
/// Bypasses ISP DNS blocking by sending queries over encrypted HTTPS
/// </summary>
public class DoHResolver : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _serverUrl;
    private readonly DnsCache _cache;
    private bool _disposed;

    public event Action<string>? OnLog;

    /// <summary>
    /// Create a new DoH resolver
    /// </summary>
    /// <param name="serverUrl">DoH server URL (default: Cloudflare)</param>
    /// <param name="cache">Optional DNS cache instance</param>
    public DoHResolver(string serverUrl = "https://cloudflare-dns.com/dns-query", DnsCache? cache = null)
    {
        _serverUrl = serverUrl;
        _cache = cache ?? new DnsCache();
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public string ServerUrl => _serverUrl;

    // HTTP/2 when the server offers it: some DoH servers (Quad9) reject HTTP/1.1
    // outright, and one multiplexed connection suits a stream of small queries.
    private static HttpRequestMessage CreateRequest(HttpMethod method, string url) => new(method, url)
    {
        Version = System.Net.HttpVersion.Version20,
        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
    };

    /// <summary>
    /// Sends a DNS message in wire format (RFC 8484 POST, application/dns-message) and
    /// returns the wire-format response, or null on any failure. Any query type works -
    /// nothing is parsed or re-encoded.
    /// </summary>
    public async Task<byte[]?> QueryWireAsync(ReadOnlyMemory<byte> query, CancellationToken ct = default)
    {
        try
        {
            using var content = new ReadOnlyMemoryContent(query);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            using var request = CreateRequest(HttpMethod.Post, _serverUrl);
            request.Content = content;
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsByteArrayAsync(ct);
            return body.Length >= 12 ? body : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve a domain to IPv4 addresses using DoH
    /// </summary>
    public async Task<IPAddress[]> ResolveAsync(string domain, bool useCache = true, CancellationToken ct = default)
    {
        // Check cache first
        if (useCache)
        {
            var cached = _cache.Get(domain);
            if (cached != null)
            {
                OnLog?.Invoke($"[DoH] {domain} -> {FormatAddresses(cached)} (cached)");
                return cached;
            }
        }

        try
        {
            // Start both A and AAAA queries in parallel for maximum speed
            var ipv4Task = FetchRecordsAsync(domain, DnsMessage.TypeA, ct);
            var ipv6Task = FetchRecordsAsync(domain, DnsMessage.TypeAaaa, ct);

            await Task.WhenAll(ipv4Task, ipv6Task);

            var ipv4Addresses = await ipv4Task;
            var ipv6Addresses = await ipv6Task;

            // COMBINE & PRIORITIZE: Place IPv6 addresses at the front of the array
            // This forces the engine to try the "empty highway" first.
            var allAddresses = ipv6Addresses.Concat(ipv4Addresses).ToArray();

            if (allAddresses.Length == 0)
            {
                OnLog?.Invoke($"[DoH] {domain} -> no records found");
                return [];
            }

            // Cache the combined results
            _cache.Set(domain, allAddresses, 300);

            OnLog?.Invoke($"[DoH] {domain} -> Found {ipv6Addresses.Length} IPv6 and {ipv4Addresses.Length} IPv4 records");
            return allAddresses;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[DoH] {domain} -> error: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Helper method to fetch specific record types from the DoH server.
    /// </summary>
    private async Task<IPAddress[]> FetchRecordsAsync(string domain, ushort type, CancellationToken ct)
    {
        var response = await QueryWireAsync(DnsMessage.BuildQuery(domain, type), ct);
        return response == null ? [] : DnsMessage.ReadAddresses(response, type).Addresses;
    }

    /// <summary>
    /// Resolve a domain to IPv6 addresses using DoH
    /// </summary>
    public async Task<IPAddress[]> ResolveIPv6Async(string domain, bool useCache = true, CancellationToken ct = default)
    {
        var cacheKey = $"{domain}:AAAA";

        if (useCache)
        {
            var cached = _cache.Get(cacheKey);
            if (cached != null) return cached;
        }

        var response = await QueryWireAsync(DnsMessage.BuildQuery(domain, DnsMessage.TypeAaaa), ct);
        if (response == null) return [];

        var (addresses, ttl) = DnsMessage.ReadAddresses(response, DnsMessage.TypeAaaa);
        if (addresses.Length > 0) _cache.Set(cacheKey, addresses, ttl);
        return addresses;
    }

    /// <summary>
    /// Resolve multiple domains in parallel
    /// </summary>
    public async Task<Dictionary<string, IPAddress[]>> ResolveBulkAsync(
        IEnumerable<string> domains, 
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, IPAddress[]>();
        var tasks = domains.Select(async d =>
        {
            var ips = await ResolveAsync(d, useCache: true, ct);
            return (Domain: d, Addresses: ips);
        });

        var resolved = await Task.WhenAll(tasks);
        
        foreach (var (domain, addresses) in resolved)
        {
            results[domain] = addresses;
        }

        return results;
    }

    /// <summary>
    /// Check if a domain might be blocked by comparing ISP DNS with DoH results
    /// </summary>
    public async Task<bool> IsBlockedAsync(string domain, CancellationToken ct = default)
    {
        try
        {
            var dohAddresses = await ResolveAsync(domain, useCache: false, ct);
            var systemAddresses = await System.Net.Dns.GetHostAddressesAsync(domain, ct);

            if (dohAddresses.Length == 0)
                return false;

            var dohSet = new HashSet<string>(dohAddresses.Select(a => a.ToString()));
            var systemSet = new HashSet<string>(systemAddresses.Select(a => a.ToString()));

            return !dohSet.SetEquals(systemSet);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the underlying cache
    /// </summary>
    public DnsCache Cache => _cache;

    private static string FormatAddresses(IPAddress[] addresses)
    {
        return addresses.Length switch
        {
            0 => "none",
            1 => addresses[0].ToString(),
            _ => $"{addresses[0]} (+{addresses.Length - 1} more)"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
