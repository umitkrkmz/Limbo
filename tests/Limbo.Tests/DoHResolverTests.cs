using Limbo.Core.Dns;

namespace Limbo.Tests;

public class DoHResolverTests
{
    [Fact]
    public void DnsCache_Set_And_Get_WorksCorrectly()
    {
        // Arrange
        var cache = new DnsCache();
        var addresses = new[] { System.Net.IPAddress.Parse("1.1.1.1") };

        // Act
        cache.Set("test.com", addresses, 300);
        var result = cache.Get("test.com");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("1.1.1.1", result[0].ToString());
    }

    [Fact]
    public void DnsCache_Get_ExpiredEntry_ReturnsNull()
    {
        // Arrange
        var cache = new DnsCache();
        var addresses = new[] { System.Net.IPAddress.Parse("1.1.1.1") };

        // Act - set with 0 TTL (already expired)
        cache.Set("expired.com", addresses, 0);
        
        // Allow time for expiry
        Thread.Sleep(100);
        var result = cache.Get("expired.com");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void DnsCache_Get_NonExistentKey_ReturnsNull()
    {
        // Arrange
        var cache = new DnsCache();

        // Act
        var result = cache.Get("nonexistent.com");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void DnsCache_MultipleEntries_IndependentExpiry()
    {
        // Arrange
        var cache = new DnsCache();
        var addr1 = new[] { System.Net.IPAddress.Parse("1.1.1.1") };
        var addr2 = new[] { System.Net.IPAddress.Parse("8.8.8.8") };

        // Act
        cache.Set("short.com", addr1, 0);   // expires immediately
        cache.Set("long.com", addr2, 3600); // long TTL

        Thread.Sleep(100);

        // Assert
        Assert.Null(cache.Get("short.com"));    // expired
        Assert.NotNull(cache.Get("long.com"));  // still valid
    }

    [Fact]
    public async Task ResolveAsync_InvalidDomain_ReturnsEmpty()
    {
        // Arrange - this test works even without internet (exception → empty array)
        var resolver = new DoHResolver();

        // Act
        var result = await resolver.ResolveAsync("this-domain-does-not-exist-12345.invalid");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAsync_UsesCache_WhenAvailable()
    {
        // Arrange - pre-populate cache to avoid network calls
        var cache = new DnsCache();
        var expected = new[] { System.Net.IPAddress.Parse("93.184.216.34") };
        cache.Set("example.com", expected, 3600);

        var resolver = new DoHResolver(cache: cache);

        // Act
        var result = await resolver.ResolveAsync("example.com", useCache: true);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal("93.184.216.34", result[0].ToString());
    }

    [Fact]
    public async Task ResolveAsync_SkipsCache_WhenDisabled()
    {
        // Arrange - pre-populate cache with fake data
        var cache = new DnsCache();
        var fakeAddr = new[] { System.Net.IPAddress.Parse("127.0.0.1") };
        cache.Set("google.com", fakeAddr, 3600);

        var resolver = new DoHResolver(cache: cache);

        // Act - resolve with cache disabled (hits real DNS or returns empty on no network)
        var result = await resolver.ResolveAsync("google.com", useCache: false);

        // Assert - either gets real result (different from fake) or empty (no network)
        if (result.Length > 0)
        {
            // If network is available, result should differ from our fake entry
            Assert.DoesNotContain(result, ip => ip.ToString() == "127.0.0.1");
        }
        // If no network, empty is acceptable
    }

    [Fact]
    public void DoHResolver_Dispose_DoesNotThrow()
    {
        // Arrange
        var resolver = new DoHResolver();

        // Act & Assert - dispose should not throw
        resolver.Dispose();
        resolver.Dispose(); // double-dispose should also be safe
    }
}
