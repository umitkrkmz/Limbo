using Limbo.Core.Profiles;
using Limbo.Core.Bypass;

namespace Limbo.Tests;

public class ProfileManagerTests
{
    // Use unique temp directories for each test to avoid conflicts
    private static string GetTempProfileDir() => 
        Path.Combine(Path.GetTempPath(), $"Limbo_Tests_{Guid.NewGuid():N}");

    [Fact]
    public void GetProfile_ExactMatch_ReturnsProfile()
    {
        // Arrange - use temp directory to avoid default profiles
        var tempDir = GetTempProfileDir();
        try
        {
            var manager = new ProfileManager(tempDir);
            var profile = new DomainProfile
            {
                Domain = "test.example.com",
                Method = BypassMethod.TcpFragmentation,
                FragmentSize = 2
            };
            manager.SaveProfile(profile);

            // Act
            var result = manager.GetProfile("test.example.com");

            // Assert
            Assert.Equal(BypassMethod.TcpFragmentation, result.Method);
            Assert.Equal(2, result.FragmentSize);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetProfile_WildcardMatch_ReturnsProfile()
    {
        // Arrange - use unique temp directory
        var tempDir = GetTempProfileDir();
        try
        {
            var manager = new ProfileManager(tempDir);
            var profile = new DomainProfile
            {
                Domain = "*.example.org",
                Method = BypassMethod.SmartFragmentation,
                FragmentSize = 3
            };
            manager.SaveProfile(profile);

            // Act
            var result = manager.GetProfile("gateway.example.org");

            // Assert
            Assert.Equal(BypassMethod.SmartFragmentation, result.Method);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("discord.com", BypassMethod.TcpFragmentation)]       // apex is covered by the wildcard
    [InlineData("DISCORD.com", BypassMethod.TcpFragmentation)]
    [InlineData("gateway.discord.com", BypassMethod.TcpFragmentation)]
    [InlineData("notdiscord.com", BypassMethod.None)]                // no partial-label matches
    [InlineData("discord.com.evil.test", BypassMethod.None)]
    public void GetProfile_WildcardCoversApexButNotLookalikes(string domain, BypassMethod expected)
    {
        var tempDir = GetTempProfileDir();
        try
        {
            var manager = new ProfileManager(tempDir);
            manager.SaveProfile(new DomainProfile { Domain = "*.discord.com", Method = BypassMethod.TcpFragmentation });

            Assert.Equal(expected, manager.GetProfile(domain).Method);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetProfile_NoMatch_ReturnsDefault()
    {
        // Arrange
        var tempDir = GetTempProfileDir();
        try
        {
            var manager = new ProfileManager(tempDir);

            // Act
            var result = manager.GetProfile("unknown.example.net");

            // Assert
            Assert.Equal(BypassMethod.None, result.Method);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetProfile_ExactMatchTakesPrecedence()
    {
        // Arrange
        var tempDir = GetTempProfileDir();
        try
        {
            var manager = new ProfileManager(tempDir);
            manager.SaveProfile(new DomainProfile
            {
                Domain = "*.test.io",
                Method = BypassMethod.TcpFragmentation
            });
            manager.SaveProfile(new DomainProfile
            {
                Domain = "gateway.test.io",
                Method = BypassMethod.Desync
            });

            // Act
            var result = manager.GetProfile("gateway.test.io");

            // Assert
            Assert.Equal(BypassMethod.Desync, result.Method);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
