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
    /// Default bypass method for unknown domains
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BypassMethod DefaultMethod { get; set; } = BypassMethod.None;

    /// <summary>
    /// Domain profiles
    /// </summary>
    public List<ProfileConfig> Profiles { get; set; } = [];

    /// <summary>
    /// Stats update interval in milliseconds
    /// </summary>
    public int StatsIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Minimize to system tray instead of taskbar
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Close to system tray instead of exiting
    /// </summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Start application minimized to tray
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// UI theme: System, Light, or Dark
    /// </summary>
    public string Theme { get; set; } = "System";
}



public class DnsSettings
{
    /// <summary>
    /// Use DNS over HTTPS
    /// </summary>
    public bool UseDoH { get; set; } = false;

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
        if (!File.Exists(path))
        {
            var config = CreateDefault();
            Save(config, path);
            return config;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LimboConfig>(json, JsonOptions) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    /// <summary>
    /// Save config to default path
    /// </summary>
    public static void Save(LimboConfig config)
    {
        Save(config, DefaultConfigPath);
    }

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
            DefaultMethod = BypassMethod.None,
            Dns = new DnsSettings
            {
                UseDoH = false,
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

    /// <summary>
    /// Get the default config file path
    /// </summary>
    public static string GetDefaultPath() => DefaultConfigPath;
}
