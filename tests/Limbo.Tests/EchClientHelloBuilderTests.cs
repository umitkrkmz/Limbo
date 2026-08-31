using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Limbo.Core.Ech;

namespace Limbo.Tests;

/// <summary>
/// Structural checks for EchClientHelloBuilder's TLS record surgery -
/// specifically the ECH-extension append and length-field patching that
/// HPKE's AAD construction now depends on being correct.
/// </summary>
public class EchClientHelloBuilderTests
{
    [Fact]
    public void WrapClientHello_ProducesStructurallyConsistentOuterHello()
    {
        const string realSni = "blocked.example.com";
        var original = BuildMinimalClientHello(realSni);

        var echConfig = new EchConfig
        {
            ConfigId = 7,
            PublicName = "cloudflare-ech.com",
            KeyConfig = new HpkeKeyConfig
            {
                KemId = 0x0020,
                PublicKey = RandomNumberGenerator.GetBytes(32),
                CipherSuites = [new HpkeCipherSuite { KdfId = 0x0001, AeadId = 0x0001 }]
            },
            RawConfig = RandomNumberGenerator.GetBytes(40)
        };

        var wrapped = EchClientHelloBuilder.WrapClientHello(original, echConfig, realSni);

        Assert.NotNull(wrapped);

        // TLS record length field must match the actual remaining bytes.
        var recordLen = BinaryPrimitives.ReadUInt16BigEndian(wrapped.AsSpan(3));
        Assert.Equal(wrapped.Length - 5, recordLen);

        // Handshake message length (3 bytes) must match the remaining bytes after its own header.
        var handshakeLen = (wrapped[6] << 16) | (wrapped[7] << 8) | wrapped[8];
        Assert.Equal(wrapped.Length - 9, handshakeLen);

        // The public_name (not the real SNI) must appear in the outer ClientHello's SNI.
        var wrappedText = Convert.ToHexString(wrapped);
        Assert.Contains(Convert.ToHexString(Encoding.ASCII.GetBytes("cloudflare-ech.com")), wrappedText);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(realSni)), wrappedText);

        // The ECH extension (type 0xfe0d) must be present with a self-consistent length.
        var (echFound, echDataLen, remaining) = FindExtension(wrapped, 0xfe0d);
        Assert.True(echFound);
        Assert.True(echDataLen > 0);
        Assert.Equal(0, remaining); // ECH must be the last extension and consume exactly the rest of the buffer
    }

    private static byte[] BuildMinimalClientHello(string sni)
    {
        var sniExt = BuildSniExtension(sni);

        var body = new List<byte>
        {
            0x03, 0x03, // client_version
        };
        body.AddRange(new byte[32]); // random
        body.Add(0x00); // session_id length
        body.Add(0x00); body.Add(0x02); // cipher_suites length
        body.Add(0x13); body.Add(0x01); // TLS_AES_128_GCM_SHA256
        body.Add(0x01); body.Add(0x00); // compression_methods

        var extensionsLen = sniExt.Length;
        body.Add((byte)(extensionsLen >> 8));
        body.Add((byte)(extensionsLen & 0xFF));
        body.AddRange(sniExt);

        var handshake = new List<byte> { 0x01, 0x00, 0x00, 0x00 }; // type + length placeholder
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

    /// <summary>
    /// Walks the extensions list of a ClientHello looking for a given extension
    /// type. Returns whether it was found, its declared data length, and how
    /// many bytes remained in the extensions list after it (0 = it was last).
    /// </summary>
    private static (bool Found, int DataLength, int RemainingBytes) FindExtension(byte[] clientHello, ushort type)
    {
        var offset = 5 + 4 + 2 + 32;
        var sessionIdLen = clientHello[offset];
        offset += 1 + sessionIdLen;
        var cipherSuitesLen = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(offset));
        offset += 2 + cipherSuitesLen;
        var compressionLen = clientHello[offset];
        offset += 1 + compressionLen;

        var extensionsLen = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(offset));
        offset += 2;
        var extensionsEnd = offset + extensionsLen;

        Assert.Equal(clientHello.Length, extensionsEnd); // no trailing/missing bytes

        while (offset + 4 <= extensionsEnd)
        {
            var extType = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(offset));
            var extLen = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(offset + 2));

            if (extType == type)
            {
                return (true, extLen, extensionsEnd - (offset + 4 + extLen));
            }

            offset += 4 + extLen;
        }

        return (false, 0, 0);
    }
}
