using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Limbo.Core.Analysis;

/// <summary>
/// Tracks outbound connections to detect "Silent Drops" (Blackhole) by DPI.
/// If an outbound packet is sent but no inbound response is received within the timeout,
/// it triggers the radar system.
/// </summary>
public class ConnectionTracker : IDisposable
{
    // Key: "IP:Port" -> Value: ConnectionRecord
    private readonly ConcurrentDictionary<string, ConnectionRecord> _activeConnections = new();
    
    // How long to wait before declaring a connection silently dropped (e.g., 3 seconds)
    private readonly TimeSpan _timeoutThreshold = TimeSpan.FromSeconds(3);
    private readonly CancellationTokenSource _cts = new();
    
    // RADAR EVENT: Fired when a silent drop is detected
    public event Action<string>? OnSilentDropDetected;

    public ConnectionTracker()
    {
        // Start the background sweeper task immediately
        Task.Run(SweeperLoop);
    }

    /// <summary>
    /// Call this when an outbound ClientHello or HTTP request is sent.
    /// </summary>
    public void TrackOutbound(string targetIp, ushort targetPort, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        
        var key = $"{targetIp}:{targetPort}";
        
        _activeConnections[key] = new ConnectionRecord 
        { 
            Domain = domain, 
            LastSeen = DateTime.UtcNow 
        };
    }

    /// <summary>
    /// Call this when ANY inbound packet is received from a target.
    /// This proves the connection is NOT blackholed.
    /// </summary>
    public void RegisterInbound(string sourceIp, ushort sourcePort)
    {
        var key = $"{sourceIp}:{sourcePort}";
        
        // If we get a response, remove it from the tracking list (it's safe)
        _activeConnections.TryRemove(key, out _);
    }

    /// <summary>
    /// Background task that sweeps the dictionary every second looking for expired connections.
    /// </summary>
    private async Task SweeperLoop()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                
                foreach (var kvp in _activeConnections)
                {
                    if (now - kvp.Value.LastSeen > _timeoutThreshold)
                    {
                        // SILENT DROP DETECTED!
                        if (_activeConnections.TryRemove(kvp.Key, out var record))
                        {
                            OnSilentDropDetected?.Invoke(record.Domain);
                        }
                    }
                }
                
                // Sweep every 1 second
                await Task.Delay(1000, _cts.Token);
            }
        }
        catch (TaskCanceledException) { /* Normal shutdown */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _activeConnections.Clear();
    }
}

/// <summary>
/// State record for an active outbound connection
/// </summary>
public class ConnectionRecord
{
    public required string Domain { get; init; }
    public required DateTime LastSeen { get; init; }
}