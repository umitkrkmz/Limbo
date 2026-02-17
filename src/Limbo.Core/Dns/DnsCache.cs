using System.Collections.Concurrent;
using System.Net;

namespace Limbo.Core.Dns;

/// <summary>
/// Thread-safe DNS cache with TTL support
/// </summary>
public class DnsCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly int _defaultTtlSeconds;

    public DnsCache(int defaultTtlSeconds = 300)
    {
        _defaultTtlSeconds = defaultTtlSeconds;
    }

    /// <summary>
    /// Get cached IP addresses for a domain
    /// </summary>
    public IPAddress[]? Get(string domain)
    {
        var key = NormalizeKey(domain);
        
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow < entry.Expiry)
            {
                return entry.Addresses;
            }
            
            // Expired, remove it
            _cache.TryRemove(key, out _);
        }
        
        return null;
    }

    /// <summary>
    /// Cache IP addresses for a domain
    /// </summary>
    public void Set(string domain, IPAddress[] addresses, int? ttlSeconds = null)
    {
        var key = NormalizeKey(domain);
        var ttl = ttlSeconds ?? _defaultTtlSeconds;
        
        _cache[key] = new CacheEntry
        {
            Addresses = addresses,
            Expiry = DateTime.UtcNow.AddSeconds(ttl)
        };
    }

    /// <summary>
    /// Check if domain is cached and not expired
    /// </summary>
    public bool IsCached(string domain)
    {
        return Get(domain) != null;
    }

    /// <summary>
    /// Remove a domain from cache
    /// </summary>
    public void Remove(string domain)
    {
        _cache.TryRemove(NormalizeKey(domain), out _);
    }

    /// <summary>
    /// Clear all cached entries
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public (int total, int expired) GetStats()
    {
        var now = DateTime.UtcNow;
        var total = _cache.Count;
        var expired = _cache.Values.Count(e => e.Expiry < now);
        return (total, expired);
    }

    private static string NormalizeKey(string domain)
    {
        return domain.ToLowerInvariant().TrimEnd('.');
    }

    private class CacheEntry
    {
        public required IPAddress[] Addresses { get; init; }
        public required DateTime Expiry { get; init; }
    }
}
