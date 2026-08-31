using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Limbo.Driver;

namespace Limbo.Core.Engine;

/// <summary>
/// Shared low-level network probing helpers: IPv4 resolution, a lightweight
/// WinDivert permission check, and a realistic TLS connectivity test. Used by
/// both CalibrationEngine (testing bypass strategies) and NetworkAnalyzer
/// (observing baseline ISP/DPI behavior) - the plumbing is identical, only
/// what each does with the result differs.
/// </summary>
internal static class NetworkProbe
{
    public static async Task<IPAddress?> ResolveIPv4Async(string domain, CancellationToken ct)
    {
        try
        {
            // Fully qualified: Limbo.Core.Dns (the DoH resolver namespace) is
            // a sibling of this file's namespace and shadows System.Net.Dns.
            var addresses = await System.Net.Dns.GetHostAddressesAsync(domain, ct);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Lightweight permission probe - "false" matches no packets, so this only checks that WinDivert can be opened at all.</summary>
    public static bool CanOpenWinDivert()
    {
        using var probe = new WinDivertDriver();
        return probe.Open("false");
    }

    /// <summary>
    /// Connects to <paramref name="targetIp"/>, sends a realistic TLS
    /// ClientHello for <paramref name="domain"/>, and waits (up to 3s) for
    /// any response. Returns whether one arrived and how long it took - a
    /// simple, generic "did this get through" signal used both to test raw
    /// connectivity and (by callers that intercept the traffic in between)
    /// to test a bypass strategy or observe DPI behavior.
    ///
    /// Deliberately connects to a specific pre-resolved IP rather than the
    /// hostname: callers that also scope a WinDivert filter to an IP (via
    /// ResolveIPv4Async) need the actual connection to go to that exact
    /// address. TcpClient.ConnectAsync(host, port) resolves the hostname
    /// again internally, and for any domain with multiple A records (which
    /// is most CDN-fronted sites) that second resolution can legitimately
    /// return a different IP than the first - the WinDivert filter would
    /// then be watching one address while traffic flows through another,
    /// silently capturing nothing.
    /// </summary>
    public static async Task<(bool Success, int LatencyMs, string? FailureReason)> TestConnectionAsync(
        IPAddress targetIp,
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

            await client.ConnectAsync(targetIp, port, cts.Token);

            // Send TLS ClientHello and wait for response
            var stream = client.GetStream();
            var clientHello = CreateMinimalClientHello(domain);
            await stream.WriteAsync(clientHello, cts.Token);

            // Try to read server hello. ValueTask.AsTask() must only be
            // called once - calling it again to compare against `completed`,
            // or awaiting the original ValueTask afterwards, throws
            // InvalidOperationException, which used to make every single
            // calibration test fail near-instantly regardless of whether the
            // connection actually worked.
            var buffer = new byte[1024];
            var readTask = stream.ReadAsync(buffer, cts.Token).AsTask();
            var timeoutTask = Task.Delay(3000, cts.Token);

            var completed = await Task.WhenAny(readTask, timeoutTask);

            if (completed == readTask)
            {
                var bytesRead = await readTask;
                if (bytesRead > 0)
                {
                    sw.Stop();
                    return (true, (int)sw.ElapsedMilliseconds, null);
                }

                sw.Stop();
                return (false, (int)sw.ElapsedMilliseconds, "Connection closed with no data");
            }

            sw.Stop();
            return (false, (int)sw.ElapsedMilliseconds, "No response within 3s (timeout)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, (int)sw.ElapsedMilliseconds, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a TLS 1.2 ClientHello with the domain as SNI. This deliberately
    /// advertises TLS 1.2 only (no supported_versions extension) rather than
    /// attempting TLS 1.3 - offering 1.3 would require a real X25519
    /// key_share to avoid a HelloRetryRequest, while omitting
    /// supported_versions makes any TLS 1.3 server fall back to a normal
    /// TLS 1.2 handshake, which virtually every public TLS server still
    /// supports. Combined with a realistic modern cipher suite list (ECDHE +
    /// AES-GCM/ChaCha20) and the extensions TLS 1.2 servers expect
    /// (supported_groups, ec_point_formats, signature_algorithms), this
    /// completes a real handshake against essentially any server.
    /// </summary>
    public static byte[] CreateMinimalClientHello(string domain)
    {
        var domainBytes = System.Text.Encoding.ASCII.GetBytes(domain);

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
            0x03, 0x03, // client_version: TLS 1.2
        };

        // Random (32 bytes) - real random, not all-zero, which some
        // strict servers/middleboxes treat as a scanner fingerprint
        var random = new byte[32];
        RandomNumberGenerator.Fill(random);
        clientHello.AddRange(random);

        // Session ID length (0 - no resumption attempt)
        clientHello.Add(0x00);

        // Cipher suites
        var cipherSuites = BuildCipherSuites();
        clientHello.Add((byte)(cipherSuites.Length >> 8));
        clientHello.Add((byte)(cipherSuites.Length & 0xFF));
        clientHello.AddRange(cipherSuites);

        // Compression methods
        clientHello.Add(0x01); // Length: 1
        clientHello.Add(0x00); // None

        // Extensions
        var extensions = new List<byte>();
        extensions.AddRange(CreateSniExtension(domainBytes));
        extensions.AddRange(BuildSupportedGroupsExtension());
        extensions.AddRange(BuildEcPointFormatsExtension());
        extensions.AddRange(BuildSignatureAlgorithmsExtension());

        clientHello.Add((byte)(extensions.Count >> 8));
        clientHello.Add((byte)(extensions.Count & 0xFF));
        clientHello.AddRange(extensions);

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

    /// <summary>Realistic cipher suite list: ECDHE key exchange with both RSA and ECDSA certificate support, AEAD ciphers preferred, two legacy CBC suites for broader compatibility.</summary>
    private static byte[] BuildCipherSuites()
    {
        ushort[] suites =
        [
            0xC02B, // TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256
            0xC02C, // TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384
            0xC02F, // TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256
            0xC030, // TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384
            0xCCA9, // TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256
            0xCCA8, // TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256
            0xC013, // TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA (legacy fallback)
            0xC014  // TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA (legacy fallback)
        ];

        return ToBigEndianBytes(suites);
    }

    private static byte[] BuildSupportedGroupsExtension()
    {
        ushort[] groups = [0x001D, 0x0017, 0x0018]; // x25519, secp256r1, secp384r1
        var list = ToBigEndianBytes(groups);

        var data = new List<byte> { (byte)(list.Length >> 8), (byte)(list.Length & 0xFF) };
        data.AddRange(list);

        return WrapExtension(0x000A, data.ToArray());
    }

    private static byte[] BuildEcPointFormatsExtension()
    {
        byte[] data = [0x01, 0x00]; // list length 1, format: uncompressed
        return WrapExtension(0x000B, data);
    }

    private static byte[] BuildSignatureAlgorithmsExtension()
    {
        ushort[] algorithms =
        [
            0x0403, // ecdsa_secp256r1_sha256
            0x0804, // rsa_pss_rsae_sha256
            0x0401, // rsa_pkcs1_sha256
            0x0503, // ecdsa_secp384r1_sha384
            0x0805, // rsa_pss_rsae_sha384
            0x0501, // rsa_pkcs1_sha384
            0x0806, // rsa_pss_rsae_sha512
            0x0601  // rsa_pkcs1_sha512
        ];
        var list = ToBigEndianBytes(algorithms);

        var data = new List<byte> { (byte)(list.Length >> 8), (byte)(list.Length & 0xFF) };
        data.AddRange(list);

        return WrapExtension(0x000D, data.ToArray());
    }

    private static byte[] WrapExtension(ushort type, byte[] data)
    {
        var result = new byte[4 + data.Length];
        result[0] = (byte)(type >> 8);
        result[1] = (byte)(type & 0xFF);
        result[2] = (byte)(data.Length >> 8);
        result[3] = (byte)(data.Length & 0xFF);
        data.CopyTo(result, 4);
        return result;
    }

    private static byte[] ToBigEndianBytes(ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            bytes[i * 2] = (byte)(values[i] >> 8);
            bytes[i * 2 + 1] = (byte)(values[i] & 0xFF);
        }
        return bytes;
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
}
