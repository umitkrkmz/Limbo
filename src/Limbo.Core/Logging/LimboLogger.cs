using System.Collections.Concurrent;
using System.Text.Json;

namespace Limbo.Core.Logging;

/// <summary>
/// Thread-safe logger with file, console output, and structured logging
/// </summary>
public class LimboLogger : IDisposable
{
    private readonly StreamWriter? _fileWriter;
    private readonly object _lock = new();
    private readonly bool _verbose;
    private readonly ConcurrentDictionary<string, DomainStats> _domainStats = new();
    private bool _disposed;

    // Filtering
    public HashSet<string> DomainFilter { get; } = [];
    public LogLevel MinLevel { get; set; } = LogLevel.Info;
    public bool EnablePacketLogging { get; set; }
    public bool EnableJsonOutput { get; set; }

    public event Action<string, LogLevel>? OnLog;
    public event Action<LogEntry>? OnLogEntry;

    public LimboLogger(string? logFilePath = null, bool verbose = false)
    {
        _verbose = verbose;
        EnablePacketLogging = verbose;

        if (logFilePath != null)
        {
            var dir = Path.GetDirectoryName(logFilePath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _fileWriter = new StreamWriter(logFilePath, append: true)
            {
                AutoFlush = true
            };
        }

        if (verbose)
        {
            MinLevel = LogLevel.Debug;
        }
    }

    public void Info(string message) => Log(message, LogLevel.Info);
    public void Warn(string message) => Log(message, LogLevel.Warning);
    public void Error(string message) => Log(message, LogLevel.Error);
    public void Debug(string message)
    {
        if (_verbose || MinLevel == LogLevel.Debug)
            Log(message, LogLevel.Debug);
    }

    /// <summary>
    /// Log packet processing event
    /// </summary>
    public void Packet(string? domain, PacketAction action, string strategy, int packetSize = 0, int fragmentCount = 0)
    {
        if (!EnablePacketLogging) return;
        if (domain != null && DomainFilter.Count > 0 && !DomainFilter.Contains(domain)) return;

        // Update domain stats
        if (domain != null)
        {
            var stats = _domainStats.GetOrAdd(domain, _ => new DomainStats());
            stats.TotalPackets++;
            stats.TotalBytes += packetSize;
            if (action == PacketAction.Modified)
                stats.ModifiedPackets++;
        }

        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Debug,
            Category = "PACKET",
            Domain = domain,
            Action = action.ToString(),
            Strategy = strategy,
            PacketSize = packetSize,
            FragmentCount = fragmentCount
        };

        LogEntry(entry);
    }

    /// <summary>
    /// Log domain match event
    /// </summary>
    public void Match(string domain, string profilePattern, string method, int latencyMs = 0)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Info,
            Category = "MATCH",
            Domain = domain,
            Message = $"matched '{profilePattern}'",
            Strategy = method,
            LatencyMs = latencyMs
        };

        // Update stats
        var stats = _domainStats.GetOrAdd(domain, _ => new DomainStats());
        stats.MatchCount++;
        stats.LastMethod = method;
        stats.LastSeen = DateTime.UtcNow;

        LogEntry(entry);
    }

    /// <summary>
    /// Log strategy application
    /// </summary>
    public void Strategy(string domain, string strategy, bool success, string? reason = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = success ? LogLevel.Info : LogLevel.Warning,
            Category = "STRATEGY",
            Domain = domain,
            Strategy = strategy,
            Success = success,
            Message = reason
        };

        LogEntry(entry);
    }

    /// <summary>
    /// Log connection event
    /// </summary>
    public void Connection(string domain, int port, ConnectionEvent eventType, string? details = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Debug,
            Category = "CONNECTION",
            Domain = domain,
            Port = port,
            Action = eventType.ToString(),
            Message = details
        };

        LogEntry(entry);
    }

    private void LogEntry(LogEntry entry)
    {
        if (entry.Level < MinLevel) return;

        string formatted;
        if (EnableJsonOutput)
        {
            formatted = JsonSerializer.Serialize(entry);
        }
        else
        {
            var timestamp = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            var prefix = GetPrefix(entry.Level);
            formatted = $"{timestamp} {prefix} [{entry.Category}] {FormatEntry(entry)}";
        }

        lock (_lock)
        {
            WriteToConsole(formatted, entry.Level);
            _fileWriter?.WriteLine(formatted);
        }

        OnLog?.Invoke(formatted, entry.Level);
        OnLogEntry?.Invoke(entry);
    }

    private void Log(string message, LogLevel level)
    {
        if (level < MinLevel) return;

        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = "GENERAL",
            Message = message
        };

        LogEntry(entry);
    }

    private static string GetPrefix(LogLevel level) => level switch
    {
        LogLevel.Debug => "[D]",
        LogLevel.Info => "[*]",
        LogLevel.Warning => "[!]",
        LogLevel.Error => "[X]",
        _ => "[?]"
    };

    private static string FormatEntry(LogEntry entry)
    {
        var parts = new List<string>();

        if (entry.Domain != null)
            parts.Add(entry.Domain);

        if (entry.Action != null)
            parts.Add($"-> {entry.Action}");

        if (entry.Strategy != null)
            parts.Add($"({entry.Strategy})");

        if (entry.Message != null)
            parts.Add(entry.Message);

        if (entry.FragmentCount > 0)
            parts.Add($"[{entry.FragmentCount} fragments]");

        if (entry.LatencyMs > 0)
            parts.Add($"[{entry.LatencyMs}ms]");

        return string.Join(" ", parts);
    }

    private static void WriteToConsole(string message, LogLevel level)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = level switch
        {
            LogLevel.Debug => ConsoleColor.DarkGray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };
        Console.WriteLine(message);
        Console.ForegroundColor = originalColor;
    }

    /// <summary>
    /// Get statistics for a specific domain
    /// </summary>
    public DomainStats? GetDomainStats(string domain)
    {
        return _domainStats.GetValueOrDefault(domain);
    }

    /// <summary>
    /// Get all domain statistics
    /// </summary>
    public IReadOnlyDictionary<string, DomainStats> GetAllStats()
    {
        return _domainStats;
    }

    /// <summary>
    /// Export logs to JSON file
    /// </summary>
    public void ExportStats(string filePath)
    {
        var json = JsonSerializer.Serialize(_domainStats, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _fileWriter?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

public enum PacketAction
{
    Passthrough,
    Matched,
    Modified,
    Dropped
}

public enum ConnectionEvent
{
    SynSent,
    Established,
    TlsClientHello,
    TlsServerHello,
    QuicInitial,
    Closed
}

/// <summary>
/// Structured log entry
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Category { get; init; } = "";
    public string? Domain { get; init; }
    public string? Action { get; init; }
    public string? Strategy { get; init; }
    public string? Message { get; init; }
    public int PacketSize { get; init; }
    public int FragmentCount { get; init; }
    public int LatencyMs { get; init; }
    public int Port { get; init; }
    public bool Success { get; init; } = true;
}

/// <summary>
/// Per-domain statistics
/// </summary>
public class DomainStats
{
    public long TotalPackets { get; set; }
    public long TotalBytes { get; set; }
    public long ModifiedPackets { get; set; }
    public long MatchCount { get; set; }
    public string? LastMethod { get; set; }
    public DateTime LastSeen { get; set; }
}
