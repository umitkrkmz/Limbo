using Limbo.Core.Detection;

namespace Limbo.Tests;

public class TlsClientHelloTests
{
    private const string Host = "gateway.discord.gg";

    [Theory]
    [InlineData(false, true)]  // classic, small
    [InlineData(true, true)]   // Chrome-like with a ~1.2 KB ML-KEM key_share, SNI first
    [InlineData(true, false)]  // ... SNI after the big key_share (Chrome randomizes the order)
    public void TryFindServerName_FindsTheHost(bool postQuantum, bool sniFirst)
    {
        var record = ClientHelloBuilder.Build(postQuantum, sniFirst, Host);

        Assert.True(TlsClientHello.TryFindServerName(record, out var offset, out var length));
        Assert.Equal(Host, System.Text.Encoding.ASCII.GetString(record, offset, length));
        Assert.Equal(Host, PacketInspector.ExtractSni(record));
    }

    [Fact]
    public void ServerNameListLengthIsNotTrusted()
    {
        // A wrong server_name_list length made the old SIMD byte scan reject the real
        // SNI and lock onto a lookalike pattern elsewhere; walking the structure doesn't care.
        var record = ClientHelloBuilder.Build(postQuantum: true, sniFirst: true, Host, corruptListLength: true);

        Assert.Equal(Host, PacketInspector.ExtractSni(record));
    }

    [Fact]
    public void HostCutOffByTheSegmentEnd_StartIsFoundButNotTheWholeName()
    {
        var record = ClientHelloBuilder.Build(postQuantum: true, sniFirst: false, Host);
        Assert.True(TlsClientHello.TryFindServerName(record, out var offset, out _));

        var firstSegment = record[..(offset + 5)]; // "gatew" - rest is in the next segment

        Assert.True(TlsClientHello.TryFindServerName(firstSegment, out var cutOffset, out var cutLength));
        Assert.Equal(offset, cutOffset);
        Assert.Equal(Host.Length, cutLength);
        Assert.Null(PacketInspector.ExtractSni(firstSegment));
    }

    [Fact]
    public void SniInALaterSegment_IsNotFound()
    {
        var record = ClientHelloBuilder.Build(postQuantum: true, sniFirst: false, Host);
        Assert.True(TlsClientHello.TryFindServerName(record, out var offset, out _));

        Assert.False(TlsClientHello.TryFindServerName(record[..(offset - 20)], out _, out _));
    }

    [Fact]
    public void Garbage_IsNotFoundWithoutThrowing()
    {
        var random = new byte[2000];
        new Random(7).NextBytes(random);
        random[0] = 0x16;
        random[5] = 0x01;

        foreach (var length in new[] { 0, 10, 43, 44, 60, 200, 2000 })
        {
            _ = TlsClientHello.TryFindServerName(random.AsSpan(0, length), out _, out _);
            Assert.Null(PacketInspector.ExtractSni(random.AsSpan(0, Math.Min(length, 43))));
        }
    }

    [Fact]
    public void FindExtension_LocatesAnyExtensionWithItsFullLength()
    {
        var record = ClientHelloBuilder.Build(postQuantum: true, sniFirst: true, Host);

        var (offset, length) = TlsClientHello.FindExtension(record, 0x0033); // key_share

        Assert.True(offset > 0);
        Assert.Equal(0x00, record[offset]);
        Assert.Equal(0x33, record[offset + 1]);
        Assert.Equal(4 + 2 + 2 + 2 + 1216 + 32, length); // header + client_shares len + group + key len + key
        Assert.Equal((-1, 0), TlsClientHello.FindExtension(record, 0x4242));
    }
}

/// <summary>Realistic TLS 1.3 ClientHello records, including Chrome's post-quantum key_share.</summary>
internal static class ClientHelloBuilder
{
    public static byte[] Build(bool postQuantum, bool sniFirst, string host, bool corruptListLength = false)
    {
        var name = System.Text.Encoding.ASCII.GetBytes(host);
        var listLength = 1 + 2 + name.Length + (corruptListLength ? 2 : 0);
        var sni = Ext(0x0000, [.. U16(listLength), 0x00, .. U16(name.Length), .. name]);

        var keyShareData = new byte[postQuantum ? 1216 + 32 : 32];
        new Random(42).NextBytes(keyShareData);
        var keyShare = Ext(0x0033, [.. U16(keyShareData.Length + 4), .. U16(postQuantum ? 0x11EC : 0x001D), .. U16(keyShareData.Length), .. keyShareData]);

        var others = new List<byte[]>
        {
            Ext(0x0a0a, []),
            Ext(0x0017, []),
            Ext(0xff01, [0x00]),
            Ext(0x000a, [.. U16(6), 0x11, 0xEC, 0x00, 0x1d, 0x00, 0x17]),
            Ext(0x0010, [.. U16(3), 0x02, (byte)'h', (byte)'2']),
            Ext(0x0005, [0x01, 0x00, 0x00, 0x00, 0x00]),
            Ext(0x002b, [0x04, 0x03, 0x04, 0x03, 0x03]),
        };

        var extensions = new List<byte[]>();
        if (sniFirst) { extensions.Add(sni); extensions.AddRange(others); extensions.Add(keyShare); }
        else { extensions.AddRange(others.Take(3)); extensions.Add(keyShare); extensions.AddRange(others.Skip(3)); extensions.Add(sni); }
        var extensionBytes = extensions.SelectMany(e => e).ToArray();

        var random = new byte[32];
        var sessionId = new byte[32];
        byte[] ciphers = [0x13, 0x01, 0x13, 0x02, 0x13, 0x03, 0xc0, 0x2b, 0xc0, 0x2f];

        byte[] body = [0x03, 0x03, .. random, 32, .. sessionId, .. U16(ciphers.Length), .. ciphers, 0x01, 0x00, .. U16(extensionBytes.Length), .. extensionBytes];
        byte[] handshake = [0x01, (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length, .. body];
        return [0x16, 0x03, 0x01, .. U16(handshake.Length), .. handshake];
    }

    private static byte[] Ext(int type, byte[] data) => [.. U16(type), .. U16(data.Length), .. data];
    private static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];
}
