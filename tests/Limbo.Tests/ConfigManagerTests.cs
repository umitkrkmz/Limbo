using Limbo.Core.Config;

namespace Limbo.Tests;

public class ConfigManagerTests
{
    private static string GetTempConfigPath() =>
        Path.Combine(Path.GetTempPath(), "LimboTests", Guid.NewGuid().ToString("N"), "custom.json");

    [Fact]
    public void Save_WritesBackToTheFileTheConfigWasLoadedFrom()
    {
        var path = GetTempConfigPath();
        try
        {
            var config = ConfigManager.Load(path);
            Assert.Equal(path, ConfigManager.GetPath(config));

            config.Profiles.Add(new ProfileConfig { Domain = "*.custom-config.test" });
            ConfigManager.Save(config);

            var reloaded = ConfigManager.Load(path);
            Assert.Contains(reloaded.Profiles, p => p.Domain == "*.custom-config.test");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void SourcePath_IsNotWrittenIntoTheConfigFile()
    {
        var path = GetTempConfigPath();
        try
        {
            var config = ConfigManager.Load(path);
            ConfigManager.Save(config);

            Assert.DoesNotContain("sourcePath", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void CorruptFile_StillSavesBackToItsOwnPath()
    {
        var path = GetTempConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "{ not json");

            var config = ConfigManager.Load(path);

            Assert.Equal(path, ConfigManager.GetPath(config));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
