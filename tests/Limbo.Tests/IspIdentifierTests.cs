using System.Net;
using Limbo.Core.Analysis;

namespace Limbo.Tests;

/// <summary>
/// Network-tolerant, matching DoHResolverTests.cs's established pattern for
/// tests that depend on a real external call: assert on structure/validity
/// when a result comes back, but don't fail the suite if the sandbox
/// running these tests has no outbound internet access.
/// </summary>
public class IspIdentifierTests
{
    [Fact]
    public async Task GetPublicNetworkInfoAsync_ReturnsNullOrAValidLookingResult()
    {
        using var identifier = new IspIdentifier();

        var result = await identifier.GetPublicNetworkInfoAsync();

        if (result == null)
        {
            // No network / lookup service unreachable - acceptable in a sandboxed CI runner.
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(result.IpAddress));
        Assert.True(IPAddress.TryParse(result.IpAddress, out _), $"'{result.IpAddress}' is not a valid IP address");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var identifier = new IspIdentifier();

        identifier.Dispose();
        identifier.Dispose(); // double-dispose should also be safe
    }
}
