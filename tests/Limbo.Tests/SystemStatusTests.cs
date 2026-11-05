using Limbo.Console.Interactive;

namespace Limbo.Tests;

public class SystemStatusTests
{
    [Fact]
    public void SplitByFamily_SeparatesVersionsDedupesAndDropsScope()
    {
        // As Windows reported it on a machine whose router hands out IPv6 DNS.
        var (v4, v6) = SystemStatus.SplitByFamily(
            ["fe80::fe9f:2aff:fe5b:35d8%13", "192.168.1.1", "fe80::fe9f:2aff:fe5b:35d8%13", "1.1.1.1"]);

        Assert.Equal(["192.168.1.1", "1.1.1.1"], v4);
        Assert.Equal(["fe80::fe9f:2aff:fe5b:35d8"], v6);
    }

    [Fact]
    public void SplitByFamily_Empty()
    {
        var (v4, v6) = SystemStatus.SplitByFamily([]);
        Assert.Empty(v4);
        Assert.Empty(v6);
    }
}
