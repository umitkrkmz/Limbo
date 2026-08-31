using System.Text.Json;
using System.Text.Json.Serialization;

namespace Limbo.Core.Analysis;

/// <summary>
/// Identifies the user's own public IP/ISP by querying a public IP-info
/// service (no local network setting reveals which ISP owns the WAN
/// connection). Looking up one's own public IP is exactly what every
/// website already sees on every visit, so this carries no extra privacy
/// cost beyond a single outbound HTTPS request.
/// </summary>
public class IspIdentifier : IDisposable
{
    private const string Endpoint = "https://ipinfo.io/json";

    private readonly HttpClient _httpClient;
    private bool _disposed;

    public IspIdentifier()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>Returns null on any network/parsing failure rather than throwing - this is best-effort context, not a required step.</summary>
    public async Task<PublicNetworkInfo?> GetPublicNetworkInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(Endpoint, ct);
            var data = JsonSerializer.Deserialize<IpInfoResponse>(json, JsonOptions);
            if (data?.Ip == null)
                return null;

            return new PublicNetworkInfo
            {
                IpAddress = data.Ip,
                Organization = data.Org ?? "",
                City = data.City ?? "",
                Country = data.Country ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    private class IpInfoResponse
    {
        public string? Ip { get; set; }

        /// <summary>e.g. "AS9121 Turk Telekomunikasyon Anonim Sirketi" - ASN + ISP/org name combined.</summary>
        public string? Org { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
    }
}

/// <summary>Public-IP-based network identity: who the ISP is, roughly where the connection appears to originate from.</summary>
public record PublicNetworkInfo
{
    public required string IpAddress { get; init; }
    public required string Organization { get; init; }
    public required string City { get; init; }
    public required string Country { get; init; }
}
