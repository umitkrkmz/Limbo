using System.Text.Json;
using System.Text.Json.Serialization;
using Limbo.Core.Bypass;

namespace Limbo.Core.Config;

/// <summary>
/// Application configuration
/// </summary>
public class LimboConfig
{
    /// <summary>
    /// WinDivert filter expression
    /// </summary>
    public string Filter { get; set; } = "tcp or udp";

    /// <summary>
    /// Enable verbose logging
    /// </summary>
    public bool Verbose { get; set; } = false;

    /// <summary>
    /// Log file path (null = console only)
    /// </summary>
    public string? LogFile { get; set; }



    /// <summary>
    /// DNS settings
    /// </summary>
    public DnsSettings Dns { get; set; } = new();

    /// <summary>
    /// Method/parameter template applied when Radar auto-detects a newly
    /// blocked domain. The Domain field on this template is unused.
    /// </summary>
    public ProfileConfig DefaultMethodProfile { get; set; } = new() { Domain = "*", Method = BypassMethod.Adaptive };

    /// <summary>
    /// Domain profiles
    /// </summary>
    public List<ProfileConfig> Profiles { get; set; } = [];

    /// <summary>
    /// Interactive console UI language
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Language Language { get; set; } = Language.English;

    /// <summary>
    /// Stats update interval in milliseconds
    /// </summary>
    public int StatsIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Enable the Radar system (automatic ISP-block detection and adaptive
    /// profile creation). Disabling this still runs the bypass engine for
    /// manually configured profiles - it only turns off auto-detection.
    /// </summary>
    public bool EnableRadar { get; set; } = true;

    /// <summary>
    /// Look up the latest GitHub release when the interactive menu opens (one
    /// anonymous request; never downloads or installs anything).
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// File this config was loaded from, so <see cref="ConfigManager.Save(LimboConfig)"/>
    /// writes back to it (e.g. a `-c custom.json` config) instead of the default path.
    /// </summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }
}

public enum Language
{
    English,
    Turkish
}

public class DnsSettings
{
    /// <summary>
    /// While the engine runs, answer every app's plain DNS queries (UDP 53, IPv4 and
    /// IPv6) over DNS-over-HTTPS instead of letting the ISP resolve (and poison) them.
    /// Falls back to plain DNS when the DoH server is unreachable.
    /// </summary>
    public bool UseDoH { get; set; } = true;

    /// <summary>
    /// DoH server URL
    /// </summary>
    public string DoHServer { get; set; } = "https://1.1.1.1/dns-query";

    /// <summary>
    /// Custom DNS servers
    /// </summary>
    public List<string> CustomServers { get; set; } = [];
}

public class ProfileConfig
{
    public required string Domain { get; set; }
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BypassMethod Method { get; set; } = BypassMethod.TcpFragmentation;
    
    /// <summary>
    /// Fragment size for TCP/Smart fragmentation (bytes)
    /// </summary>
    public int FragmentSize { get; set; } = 2;

    /// <summary>
    /// Split position for Desync strategy (bytes from start)
    /// </summary>
    public int SplitPosition { get; set; } = 3;

    /// <summary>
    /// Split offset from SNI for SmartFragmentation strategy
    /// </summary>
    public int SplitOffset { get; set; } = 3;

    /// <summary>
    /// TTL value for FakeTtl and UdpFake strategies
    /// </summary>
    public int FakeTtl { get; set; } = 3;

    /// <summary>
    /// Custom public name for ECH strategy (empty = auto-detect)
    /// </summary>
    public string EchPublicName { get; set; } = "";

    /// <summary>
    /// Fake packet size for QuicFake strategy (bytes)
    /// </summary>
    public int FakePacketSize { get; set; } = 64;

    /// <summary>
    /// Whether to use adaptive (auto-tuned) parameters
    /// </summary>
    public bool UseAdaptiveParams { get; set; } = true;
}

/// <summary>
/// Configuration manager - loads and saves config files
/// </summary>
public static class ConfigManager
{
    private static readonly string DefaultConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Limbo",
        "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Load config from default path or create default
    /// </summary>
    public static LimboConfig Load()
    {
        return Load(DefaultConfigPath);
    }

    /// <summary>
    /// Load config from specified path
    /// </summary>
    public static LimboConfig Load(string path)
    {
        LimboConfig config;
        if (!File.Exists(path))
        {
            config = CreateDefault();
            Save(config, path);
        }
        else
        {
            try
            {
                var json = File.ReadAllText(path);
                config = JsonSerializer.Deserialize<LimboConfig>(json, JsonOptions) ?? CreateDefault();
            }
            catch
            {
                config = CreateDefault();
            }
        }

        config.SourcePath = path;
        return config;
    }

    /// <summary>
    /// Save config back to the file it was loaded from (default path if it wasn't loaded from one)
    /// </summary>
    public static void Save(LimboConfig config)
    {
        Save(config, GetPath(config));
    }

    public static string GetPath(LimboConfig config) => config.SourcePath ?? DefaultConfigPath;

    /// <summary>
    /// Save config to specified path
    /// </summary>
    public static void Save(LimboConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Create default configuration
    /// </summary>
    public static LimboConfig CreateDefault()
    {
        return new LimboConfig
        {
            Filter = "tcp or udp",
            Verbose = false,
            Dns = new DnsSettings
            {
                UseDoH = true,
                DoHServer = "https://1.1.1.1/dns-query"
            },
            Profiles =
            [
                new ProfileConfig
                {
                    Domain = "*.discord.com",
                    Method = BypassMethod.TcpFragmentation,
                    FragmentSize = 2
                },
                new ProfileConfig
                {
                    Domain = "*.discord.gg",
                    Method = BypassMethod.TcpFragmentation,
                    FragmentSize = 2
                },
                // CDN/media (attachments, avatars, the updater) and voice servers -
                // without these Discord logs in but images/updates/voice stay blocked.
                new ProfileConfig
                {
                    Domain = "*.discordapp.com",
                    Method = BypassMethod.TcpFragmentation,
                    FragmentSize = 2
                },
                new ProfileConfig
                {
                    Domain = "*.discordapp.net",
                    Method = BypassMethod.TcpFragmentation,
                    FragmentSize = 2
                },
                new ProfileConfig
                {
                    Domain = "*.discord.media",
                    Method = BypassMethod.TcpFragmentation,
                    FragmentSize = 2
                },
                new ProfileConfig
                {
                    Domain = "gateway.discord.com",
                    Method = BypassMethod.Desync,
                    FragmentSize = 3
                }
            ],
            StatsIntervalMs = 1000
        };
    }
}
