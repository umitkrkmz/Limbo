using System.Buffers.Binary;
using System.Text;
using Limbo.Core.Bypass;
using Limbo.Core.Engine;
using Limbo.Core.Profiles;

namespace Limbo.Tests;

/// <summary>
/// Pure packet-processing logic of LimboEngine - the piece RunCommand used to
/// duplicate by hand. Uses a temp-directory ProfileManager (matching
/// ProfileManagerTests' convention) so nothing touches real profile data.
/// Radar/BlacklistManager wiring isn't exercised here since LimboEngine's
/// BlacklistManager isn't currently injectable with a custom path, and these
/// tests must not touch the real %LOCALAPPDATA%\Limbo\Auto-Blacklist.txt.
/// </summary>
public class LimboEngineTests
{
    private static string GetTempProfileDir() =>
        Path.Combine(Path.GetTempPath(), $"Limbo_EngineTests_{Guid.NewGuid():N}");

    [Fact]
    public void ProcessPacket_NoProfileForDomain_ReturnsUnmodifiedPassthrough()
    {
        var tempDir = GetTempProfileDir();
        try
        {
            using var engine = new LimboEngine(new ProfileManager(tempDir), enableRadar: false);
            var packet = BuildClientHelloPacket("unconfigured.example.com");

            var results = engine.ProcessPacket(packet, isOutbound: true);

            var single = Assert.Single(results);
            Assert.False(single.RecalcChecksum);
            Assert.Equal(packet.Length, single.Data.Length);
            Assert.Equal(0, engine.GetStats().MatchedPackets);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ProcessPacket_MatchingProfile_AppliesBypassAndCountsAsMatched()
    {
        var tempDir = GetTempProfileDir();
        try
        {
            var profileManager = new ProfileManager(tempDir);
            profileManager.SaveProfile(new DomainProfile
            {
                Domain = "blocked.example.com",
                Method = BypassMethod.TcpFragmentation,
                FragmentSize = 2,
                UseAdaptiveParams = false
            });

            using var engine = new LimboEngine(profileManager, enableRadar: false);
            var packet = BuildClientHelloPacket("blocked.example.com");

            var results = engine.ProcessPacket(packet, isOutbound: true);

            Assert.True(results.Count > 1, "TCP fragmentation should split the ClientHello into multiple segments");
            var stats = engine.GetStats();
            Assert.Equal(1, stats.MatchedPackets);
            Assert.Equal(1, stats.PacketsModified);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // The engine used to call the method-only overload, so a profile's parameters
    // (fragment size, TTL, split position...) were silently replaced by defaults.
    [Fact]
    public void ProcessPacket_UsesTheProfilesOwnParameters()
    {
        int SegmentsFor(int fragmentSize, bool adaptive)
        {
            var tempDir = GetTempProfileDir();
            try
            {
                var profileManager = new ProfileManager(tempDir);
                profileManager.SaveProfile(new DomainProfile
                {
                    Domain = "blocked.example.com",
                    Method = BypassMethod.TcpFragmentation,
                    FragmentSize = fragmentSize,
                    UseAdaptiveParams = adaptive
                });
                using var engine = new LimboEngine(profileManager, enableRadar: false);
                return engine.ProcessPacket(BuildClientHelloPacket("blocked.example.com"), isOutbound: true).Count;
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        Assert.NotEqual(SegmentsFor(1, adaptive: false), SegmentsFor(40, adaptive: false));
        Assert.Equal(SegmentsFor(1, adaptive: true), SegmentsFor(40, adaptive: true)); // adaptive = strategy defaults
    }

    [Theory]
    [InlineData(false, "unprofiled.example.com", false)] // profiles-only: untouched
    [InlineData(true, "unprofiled.example.com", true)]   // global: default method applied
    [InlineData(true, "excluded.example.com", false)]    // global, but a None profile opts out
    [InlineData(true, "www.excluded.example.com", false)]
    public void GlobalMode_BypassesUnprofiledDomainsExceptExclusions(bool globalMode, string domain, bool expectModified)
    {
        var tempDir = GetTempProfileDir();
        try
        {
            var profileManager = new ProfileManager(tempDir);
            profileManager.SaveProfile(new DomainProfile { Domain = "*.excluded.example.com", Method = BypassMethod.None });

            using var engine = new LimboEngine(
                profileManager,
                enableRadar: false,
                defaultMethodProfile: new Limbo.Core.Config.ProfileConfig { Domain = "*", Method = BypassMethod.TcpFragmentation },
                globalMode: globalMode);

            var results = engine.ProcessPacket(BuildClientHelloPacket(domain), isOutbound: true);

            Assert.Equal(expectModified, results.Count > 1);
            Assert.Equal(expectModified ? 1 : 0, engine.GetStats().MatchedPackets);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ProcessPacket_InboundDirection_NeverMatchesEvenWithConfiguredProfile()
    {
        var tempDir = GetTempProfileDir();
        try
        {
            var profileManager = new ProfileManager(tempDir);
            profileManager.SaveProfile(new DomainProfile { Domain = "blocked.example.com", Method = BypassMethod.TcpFragmentation });

            using var engine = new LimboEngine(profileManager, enableRadar: false);
            var packet = BuildClientHelloPacket("blocked.example.com");

            var results = engine.ProcessPacket(packet, isOutbound: false);

            var single = Assert.Single(results);
            Assert.False(single.RecalcChecksum);
            Assert.Equal(0, engine.GetStats().MatchedPackets);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    /// <summary>Minimal IPv4 + TCP + TLS ClientHello-with-SNI packet, matching the byte layout PacketInspector.Analyze/ExtractSni expect.</summary>
    private static byte[] BuildClientHelloPacket(string domain)
    {
        var tls = BuildMinimalClientHello(domain);
        var packet = new byte[40 + tls.Length]; // 20-byte IPv4 header + 20-byte TCP header + TLS payload

        packet[0] = 0x45; // IPv4, IHL=5 (20 bytes)
        packet[9] = 6;    // Protocol: TCP
        packet[20 + 12] = 0x50; // TCP data offset = 5 (20 bytes)

        tls.CopyTo(packet, 40);
        return packet;
    }

    private static byte[] BuildMinimalClientHello(string sni)
    {
        var sniExt = BuildSniExtension(sni);

        var body = new List<byte> { 0x03, 0x03 }; // client_version
        body.AddRange(new byte[32]); // random
        body.Add(0x00); // session_id length
        body.Add(0x00); body.Add(0x02); // cipher_suites length
        body.Add(0x13); body.Add(0x01); // TLS_AES_128_GCM_SHA256
        body.Add(0x01); body.Add(0x00); // compression_methods

        var extensionsLen = sniExt.Length;
        body.Add((byte)(extensionsLen >> 8));
        body.Add((byte)(extensionsLen & 0xFF));
        body.AddRange(sniExt);

        var handshake = new List<byte> { 0x01, 0x00, 0x00, 0x00 };
        var handshakeLen = body.Count;
        handshake[1] = (byte)(handshakeLen >> 16);
        handshake[2] = (byte)(handshakeLen >> 8);
        handshake[3] = (byte)handshakeLen;
        handshake.AddRange(body);

        var record = new List<byte> { 0x16, 0x03, 0x01, 0x00, 0x00 };
        var recordLen = handshake.Count;
        record[3] = (byte)(recordLen >> 8);
        record[4] = (byte)(recordLen & 0xFF);
        record.AddRange(handshake);

        return record.ToArray();
    }

    private static byte[] BuildSniExtension(string hostname)
    {
        var nameBytes = Encoding.ASCII.GetBytes(hostname);
        var result = new byte[2 + 2 + 2 + 1 + 2 + nameBytes.Length];
        var offset = 0;

        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), 0x0000);
        offset += 2;
        var dataLen = 2 + 1 + 2 + nameBytes.Length;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)dataLen);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)(1 + 2 + nameBytes.Length));
        offset += 2;
        result[offset++] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)nameBytes.Length);
        offset += 2;
        nameBytes.CopyTo(result, offset);

        return result;
    }
}
