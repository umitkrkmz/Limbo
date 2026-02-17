using System.Diagnostics;
using System.Net.Sockets;
using Limbo.Core.Bypass;
using Limbo.Core.Profiles;

namespace Limbo.Core.Engine;

/// <summary>
/// Calibration engine - automatically finds working bypass method for a domain
/// Inspired by ZeroDivert's AdaptiveEngine
/// </summary>
public class CalibrationEngine
{
    private readonly ProfileManager _profileManager;
    private readonly BypassEngine _bypassEngine;
    
    public event Action<string>? OnLog;
    public event Action<CalibrationProgress>? OnProgress;

    public CalibrationEngine(ProfileManager profileManager, BypassEngine bypassEngine)
    {
        _profileManager = profileManager;
        _bypassEngine = bypassEngine;
    }

    /// <summary>
    /// Calibrate bypass method for a specific domain
    /// </summary>
    public async Task<CalibrationResult> CalibrateAsync(
        string domain,
        int port = 443,
        CancellationToken ct = default)
    {
        OnLog?.Invoke($"Starting calibration for {domain}:{port}");
        
        var strategies = _bypassEngine.GetStrategiesInOrder();
        var results = new List<(BypassMethod Method, bool Success, int LatencyMs)>();

        // Test without any bypass first (baseline)
        OnProgress?.Invoke(new CalibrationProgress
        {
            Domain = domain,
            CurrentMethod = BypassMethod.None,
            Progress = 0,
            TotalStrategies = strategies.Count + 1
        });

        var baselineResult = await TestConnectionAsync(domain, port, ct);
        results.Add((BypassMethod.None, baselineResult.Success, baselineResult.LatencyMs));

        if (baselineResult.Success)
        {
            OnLog?.Invoke($"Direct connection works for {domain} (no bypass needed)");
            
            // Save as no-bypass profile
            var profile = new DomainProfile
            {
                Domain = domain,
                Method = BypassMethod.None,
                LastCalibrated = DateTime.UtcNow,
                SuccessRate = 100,
                IsVerified = true
            };
            _profileManager.SaveProfile(profile);

            return new CalibrationResult
            {
                Domain = domain,
                Success = true,
                BestMethod = BypassMethod.None,
                LatencyMs = baselineResult.LatencyMs,
                Message = "Direct connection works, no bypass needed"
            };
        }

        // Test each strategy
        var strategyIndex = 1;
        BypassMethod bestMethod = BypassMethod.None;
        int bestLatency = int.MaxValue;
        
        foreach (var strategy in strategies)
        {
            var method = GetMethodFromStrategy(strategy);
            
            OnProgress?.Invoke(new CalibrationProgress
            {
                Domain = domain,
                CurrentMethod = method,
                Progress = strategyIndex,
                TotalStrategies = strategies.Count + 1
            });

            OnLog?.Invoke($"Testing {strategy.Name}...");

            // For now, we simulate the test - in real implementation,
            // we would need to actually apply the bypass during the connection test
            var testResult = await TestWithBypassAsync(domain, port, method, ct);
            results.Add((method, testResult.Success, testResult.LatencyMs));

            if (testResult.Success && testResult.LatencyMs < bestLatency)
            {
                bestMethod = method;
                bestLatency = testResult.LatencyMs;
                OnLog?.Invoke($"  ✓ {strategy.Name} works! Latency: {testResult.LatencyMs}ms");
            }
            else if (testResult.Success)
            {
                OnLog?.Invoke($"  ✓ {strategy.Name} works but slower ({testResult.LatencyMs}ms)");
            }
            else
            {
                OnLog?.Invoke($"  ✗ {strategy.Name} failed");
            }

            strategyIndex++;
        }

        if (bestMethod != BypassMethod.None)
        {
            // Save successful profile
            var profile = new DomainProfile
            {
                Domain = domain,
                Method = bestMethod,
                FragmentSize = GetDefaultFragmentSize(bestMethod),
                LastCalibrated = DateTime.UtcNow,
                SuccessRate = 100,
                IsVerified = true
            };
            _profileManager.SaveProfile(profile);

            return new CalibrationResult
            {
                Domain = domain,
                Success = true,
                BestMethod = bestMethod,
                LatencyMs = bestLatency,
                Message = $"Best method: {bestMethod} ({bestLatency}ms)"
            };
        }

        // All strategies failed
        return new CalibrationResult
        {
            Domain = domain,
            Success = false,
            BestMethod = BypassMethod.Adaptive,
            LatencyMs = 0,
            Message = "All DPI bypass methods failed. IP-based blocking suspected. Try Adaptive mode."
        };
    }

    /// <summary>
    /// Quick test to check if a domain is blocked
    /// </summary>
    public async Task<bool> IsBlockedAsync(string domain, int port = 443, CancellationToken ct = default)
    {
        var result = await TestConnectionAsync(domain, port, ct);
        return !result.Success;
    }

    private async Task<(bool Success, int LatencyMs)> TestConnectionAsync(
        string domain,
        int port,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            
            await client.ConnectAsync(domain, port, cts.Token);
            
            // Send TLS ClientHello and wait for response
            var stream = client.GetStream();
            var clientHello = CreateMinimalClientHello(domain);
            await stream.WriteAsync(clientHello, cts.Token);
            
            // Try to read server hello
            var buffer = new byte[1024];
            var readTask = stream.ReadAsync(buffer, cts.Token);
            var timeoutTask = Task.Delay(3000, cts.Token);
            
            var completed = await Task.WhenAny(readTask.AsTask(), timeoutTask);
            
            if (completed == readTask.AsTask())
            {
                var bytesRead = await readTask;
                if (bytesRead > 0)
                {
                    sw.Stop();
                    return (true, (int)sw.ElapsedMilliseconds);
                }
            }
        }
        catch
        {
            // Connection failed
        }
        
        sw.Stop();
        return (false, (int)sw.ElapsedMilliseconds);
    }

    private async Task<(bool Success, int LatencyMs)> TestWithBypassAsync(
        string domain,
        int port,
        BypassMethod method,
        CancellationToken ct)
    {
        // In a real implementation, this would:
        // 1. Create a raw socket or use WinDivert to intercept
        // 2. Apply the bypass strategy to outgoing packets
        // 3. Test the connection
        
        // For now, we just test direct connection with a hint that bypass is enabled
        // The actual bypass would be applied by LimboEngine during real traffic
        
        return await TestConnectionAsync(domain, port, ct);
    }

    private static byte[] CreateMinimalClientHello(string domain)
    {
        // Create a minimal TLS 1.2 ClientHello with the domain as SNI
        var domainBytes = System.Text.Encoding.ASCII.GetBytes(domain);
        
        // This is a simplified ClientHello - real implementation would be more complete
        var clientHello = new List<byte>
        {
            // TLS record header
            0x16,       // ContentType: Handshake
            0x03, 0x01, // Version: TLS 1.0 (for handshake compatibility)
            0x00, 0x00, // Length placeholder (will be filled)
            
            // Handshake header
            0x01,       // HandshakeType: ClientHello
            0x00, 0x00, 0x00, // Length placeholder (will be filled)
            
            // ClientHello body
            0x03, 0x03, // Version: TLS 1.2
        };
        
        // Random (32 bytes)
        clientHello.AddRange(new byte[32]);
        
        // Session ID length (0)
        clientHello.Add(0x00);
        
        // Cipher suites
        clientHello.Add(0x00);
        clientHello.Add(0x02); // Length: 2
        clientHello.Add(0x00);
        clientHello.Add(0x2F); // TLS_RSA_WITH_AES_128_CBC_SHA
        
        // Compression methods
        clientHello.Add(0x01); // Length: 1
        clientHello.Add(0x00); // None
        
        // Extensions
        var sniExtension = CreateSniExtension(domainBytes);
        var extLen = sniExtension.Length;
        clientHello.Add((byte)(extLen >> 8));
        clientHello.Add((byte)(extLen & 0xFF));
        clientHello.AddRange(sniExtension);
        
        // Fix lengths
        var handshakeLen = clientHello.Count - 9;
        clientHello[6] = (byte)((handshakeLen >> 16) & 0xFF);
        clientHello[7] = (byte)((handshakeLen >> 8) & 0xFF);
        clientHello[8] = (byte)(handshakeLen & 0xFF);
        
        var recordLen = clientHello.Count - 5;
        clientHello[3] = (byte)((recordLen >> 8) & 0xFF);
        clientHello[4] = (byte)(recordLen & 0xFF);
        
        return clientHello.ToArray();
    }

    private static byte[] CreateSniExtension(byte[] domain)
    {
        var ext = new List<byte>
        {
            0x00, 0x00, // Extension type: SNI
        };
        
        var nameLen = domain.Length;
        var listLen = nameLen + 3;
        var extLen = listLen + 2;
        
        ext.Add((byte)(extLen >> 8));
        ext.Add((byte)(extLen & 0xFF));
        ext.Add((byte)(listLen >> 8));
        ext.Add((byte)(listLen & 0xFF));
        ext.Add(0x00); // Name type: hostname
        ext.Add((byte)(nameLen >> 8));
        ext.Add((byte)(nameLen & 0xFF));
        ext.AddRange(domain);
        
        return ext.ToArray();
    }

    private static BypassMethod GetMethodFromStrategy(IDpiBypassStrategy strategy)
    {
        return strategy.Name switch
        {
            "TCP Fragmentation" => BypassMethod.TcpFragmentation,
            "Smart Fragmentation" => BypassMethod.SmartFragmentation,
            "TCP Desync" => BypassMethod.Desync,
            "Fake TTL" => BypassMethod.FakeTtl,
            "UDP Fake" => BypassMethod.UdpFake,
            _ => BypassMethod.None
        };
    }

    private static int GetDefaultFragmentSize(BypassMethod method)
    {
        return method switch
        {
            BypassMethod.TcpFragmentation => 2,
            BypassMethod.SmartFragmentation => 3,
            _ => 0
        };
    }
}

/// <summary>
/// Calibration progress information
/// </summary>
public record CalibrationProgress
{
    public required string Domain { get; init; }
    public required BypassMethod CurrentMethod { get; init; }
    public required int Progress { get; init; }
    public required int TotalStrategies { get; init; }
}

/// <summary>
/// Calibration result
/// </summary>
public record CalibrationResult
{
    public required string Domain { get; init; }
    public required bool Success { get; init; }
    public required BypassMethod BestMethod { get; init; }
    public required int LatencyMs { get; init; }
    public required string Message { get; init; }
}
