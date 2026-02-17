using Limbo.Core.Analysis;

namespace Limbo.Tests;

public class BlacklistManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _blacklistPath;

    public BlacklistManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Limbo_BL_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _blacklistPath = Path.Combine(_tempDir, "Auto-Blacklist.txt");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private BlacklistManager CreateManager() => new BlacklistManager(_blacklistPath);

    [Fact]
    public void AddToBlacklist_NewDomain_IsPresent()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        manager.AddToBlacklist("blocked.example.com");
        var all = manager.GetAllBlacklisted();

        // Assert
        Assert.Contains("blocked.example.com", all);
    }

    [Fact]
    public void AddToBlacklist_DuplicateDomain_NotDuplicated()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        manager.AddToBlacklist("dupe.com");
        manager.AddToBlacklist("dupe.com");
        var all = manager.GetAllBlacklisted();

        // Assert - only one occurrence
        Assert.Single(all, d => d.Equals("dupe.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddToBlacklist_NewDomain_FiresEvent()
    {
        // Arrange
        var manager = CreateManager();
        var eventFired = false;
        manager.OnNewBlacklistedDomain += _ => eventFired = true;

        // Act
        manager.AddToBlacklist("event-test.com");

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void AddToBlacklist_DuplicateDomain_DoesNotFireEventAgain()
    {
        // Arrange
        var manager = CreateManager();
        var eventCount = 0;
        manager.OnNewBlacklistedDomain += _ => eventCount++;

        // Act
        manager.AddToBlacklist("duplicate.com");
        manager.AddToBlacklist("duplicate.com"); // second call must not fire

        // Assert
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void AddToBlacklist_IpAddress_IsIgnored()
    {
        // Arrange
        var manager = CreateManager();

        // Act - IP addresses should be filtered out (not added)
        manager.AddToBlacklist("1.2.3.4");
        manager.AddToBlacklist("::1");

        // Assert
        Assert.Empty(manager.GetAllBlacklisted());
    }

    [Fact]
    public void Export_WritesDomainsToFile()
    {
        // Arrange
        var manager = CreateManager();
        manager.AddToBlacklist("export1.com");
        manager.AddToBlacklist("export2.com");
        var exportPath = Path.Combine(_tempDir, "exported.txt");

        // Act
        manager.Export(exportPath);

        // Assert
        Assert.True(File.Exists(exportPath));
        var lines = File.ReadAllLines(exportPath);
        Assert.Contains("export1.com", lines);
        Assert.Contains("export2.com", lines);
    }

    [Fact]
    public void Import_LoadsDomainsFromFile()
    {
        // Arrange
        var importPath = Path.Combine(_tempDir, "import_source.txt");
        File.WriteAllLines(importPath, ["imported1.com", "imported2.com"]);
        var manager = CreateManager();

        // Act
        manager.Import(importPath);
        var all = manager.GetAllBlacklisted();

        // Assert
        Assert.Contains("imported1.com", all);
        Assert.Contains("imported2.com", all);
    }

    [Fact]
    public void GetAllBlacklisted_ReturnsSnapshot_NotLiveReference()
    {
        // Arrange
        var manager = CreateManager();
        manager.AddToBlacklist("snap1.com");
        manager.AddToBlacklist("snap2.com");

        // Act - capture snapshot, then add another domain
        var snapshot = manager.GetAllBlacklisted();
        manager.AddToBlacklist("snap3.com");

        // Assert - snapshot does not contain later-added domain
        Assert.DoesNotContain("snap3.com", snapshot);
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public void Persistence_SavedDomains_LoadedOnNewInstance()
    {
        // Arrange - first manager writes domain
        var manager1 = CreateManager();
        manager1.AddToBlacklist("persisted.com");

        // Act - second manager reads from same file
        var manager2 = new BlacklistManager(_blacklistPath);

        // Assert
        Assert.Contains("persisted.com", manager2.GetAllBlacklisted());
    }
}
