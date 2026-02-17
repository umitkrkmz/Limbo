using System.Net;
using System.Text.Json;

namespace Limbo.Core.Ech;

/// <summary>
/// Fetches ECH configuration from DNS HTTPS records using DoH
/// </summary>
public class EchConfigFetcher
{
    private readonly HttpClient _httpClient;
    private readonly string _dohServer;

    // Cache for ECH configs
    private static readonly Dictionary<string, (EchConfig Config, DateTime Expiry)> _cache = new();

    public EchConfigFetcher(string dohServer = "https://1.1.1.1/dns-query")
    {
        _dohServer = dohServer;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/dns-json");
    }

    /// <summary>
    /// Fetch ECH config for a domain
    /// </summary>
    public async Task<EchConfig?> FetchAsync(string domain, CancellationToken ct = default)
    {
        // Check cache first
        if (_cache.TryGetValue(domain, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return cached.Config;
        }

        try
        {
            // Query DNS HTTPS record (type 65)
            var url = $"{_dohServer}?name={domain}&type=HTTPS";
            var response = await _httpClient.GetStringAsync(url, ct);

            var json = JsonDocument.Parse(response);
            var answers = json.RootElement.GetProperty("Answer");

            foreach (var answer in answers.EnumerateArray())
            {
                if (!answer.TryGetProperty("data", out var dataElement)) continue;
                var data = dataElement.GetString();
                if (string.IsNullOrEmpty(data)) continue;

                // Parse HTTPS record data
                var echConfig = ParseHttpsRecordData(data);
                if (echConfig != null)
                {
                    // Cache for 1 hour
                    _cache[domain] = (echConfig, DateTime.UtcNow.AddHours(1));
                    return echConfig;
                }
            }
        }
        catch
        {
            // Failed to fetch
        }

        return null;
    }

    /// <summary>
    /// Parse ECH config from HTTPS record data string
    /// </summary>
    private static EchConfig? ParseHttpsRecordData(string data)
    {
        // HTTPS record format: "priority target key=value..."
        // Look for ech= parameter which contains base64 encoded ECH config

        var parts = data.Split(' ');
        foreach (var part in parts)
        {
            if (part.StartsWith("ech=", StringComparison.OrdinalIgnoreCase))
            {
                var base64 = part[4..].Trim('"');
                try
                {
                    var bytes = Convert.FromBase64String(base64);
                    return EchConfig.Parse(bytes);
                }
                catch
                {
                    continue;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get well-known ECH configs for popular CDNs
    /// </summary>
    public static EchConfig? GetWellKnownConfig(string domain)
    {
        // Cloudflare ECH config (fallback)
        if (domain.Contains("cloudflare") || 
            domain.Contains("discord") || 
            domain.Contains("twitter"))
        {
            return new EchConfig
            {
                PublicName = "cloudflare-ech.com",
                ConfigId = 0,
                KeyConfig = new HpkeKeyConfig
                {
                    KemId = 0x0020, // X25519
                    CipherSuites = [new HpkeCipherSuite { KdfId = 0x0001, AeadId = 0x0001 }]
                }
            };
        }

        return null;
    }
}
