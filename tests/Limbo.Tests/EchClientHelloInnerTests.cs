using System.Buffers.Binary;
using System.Security.Cryptography;
using Limbo.Core.Ech;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Limbo.Tests;

/// <summary>
/// Round-trip validation of EchClientHelloBuilder's ClientHelloInner. There's
/// no official ECH test vector to check against (unlike HpkeTests.cs's RFC
/// 9180 vector), so the strongest available check is decrypting the ECH
/// extension the way a real server would - server-side HPKE math via
/// HpkePrimitives, mirroring HpkeTests.cs's pattern - and verifying the
/// recovered EncodedClientHelloInner matches the original ClientHello
/// byte-for-byte except for the zeroed session_id, with correct padding.
/// </summary>
public class EchClientHelloInnerTests
{
    [Fact]
    public void WrapClientHello_InnerDecryptsToOriginalBodyWithSessionIdZeroedAndPadded()
    {
        const string realSni = "blocked.example.com";
        const string publicName = "cloudflare-ech.com";

        // Non-empty session ID so the test actually exercises the zeroing
        // transformation, not just a no-op on an already-empty field.
        var sessionId = RandomNumberGenerator.GetBytes(32);
        var original = BuildClientHello(realSni, sessionId);

        // --- Server-side keypair for this test's synthetic ECHConfig ---
        var keyGen = new X25519KeyPairGenerator();
        keyGen.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var serverKeyPair = keyGen.GenerateKeyPair();
        var serverPrivateKey = (X25519PrivateKeyParameters)serverKeyPair.Private;
        var serverPublicKey = ((X25519PublicKeyParameters)serverKeyPair.Public).GetEncoded();

        var rawConfig = RandomNumberGenerator.GetBytes(40);

        // Deliberately smaller than realSni.Length (20 chars) to exercise the
        // max(0, maximumNameLength - D) clamp branch of the padding formula.
        const byte maximumNameLength = 10;

        var echConfig = new EchConfig
        {
            ConfigId = 3,
            PublicName = publicName,
            MaximumNameLength = maximumNameLength,
            KeyConfig = new HpkeKeyConfig
            {
                KemId = 0x0020,
                PublicKey = serverPublicKey,
                CipherSuites = [new HpkeCipherSuite { KdfId = 0x0001, AeadId = 0x0001 }]
            },
            RawConfig = rawConfig
        };

        // --- Production code under test ---
        var outer = EchClientHelloBuilder.WrapClientHello(original, echConfig, realSni);
        Assert.NotNull(outer);

        var (enc, ciphertextAndTag, payloadOffset) = ExtractEchExtension(outer);

        // --- "Server-side" HPKE decrypt (mirrors HpkeTests.cs's pattern) ---
        var dh = new byte[32];
        var agreement = new X25519Agreement();
        agreement.Init(serverPrivateKey);
        agreement.CalculateAgreement(new X25519PublicKeyParameters(enc, 0), dh, 0);

        var sharedSecret = HpkePrimitives.KemExtractAndExpand(dh, enc, serverPublicKey);
        var info = Concat("tls ech\0"u8.ToArray(), rawConfig);
        var (key, baseNonce, _) = HpkePrimitives.KeySchedule(sharedSecret, info);
        var nonce = HpkePrimitives.ComputeNonce(baseNonce, 0);

        // AAD = the outer we actually got, with the ECH payload zeroed back
        // out - exactly what BuildOuterAad constructed before encryption.
        var aad = (byte[])outer.Clone();
        Array.Clear(aad, payloadOffset, ciphertextAndTag.Length);

        var ciphertext = ciphertextAndTag[..^16];
        var tag = ciphertextAndTag[^16..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);

        // --- Verify the recovered EncodedClientHelloInner ---
        Assert.Equal(0, plaintext.Length % 32); // block-aligned padding

        const int sessionIdLenOffset = 2 + 32; // client_version(2) + random(32)
        Assert.Equal(0, plaintext[sessionIdLenOffset]); // session_id zeroed

        var originalBody = original[9..]; // strip record(5) + handshake(4) headers
        var beforeSessionId = originalBody[..sessionIdLenOffset];
        var afterSessionId = originalBody[(sessionIdLenOffset + 1 + sessionId.Length)..];

        var expectedEncoded = new byte[beforeSessionId.Length + 1 + afterSessionId.Length];
        beforeSessionId.CopyTo(expectedEncoded, 0);
        expectedEncoded[beforeSessionId.Length] = 0x00;
        afterSessionId.CopyTo(expectedEncoded, beforeSessionId.Length + 1);

        Assert.True(plaintext.Length >= expectedEncoded.Length);
        Assert.Equal(expectedEncoded, plaintext[..expectedEncoded.Length]);

        for (var i = expectedEncoded.Length; i < plaintext.Length; i++)
        {
            Assert.Equal(0, plaintext[i]);
        }
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }

    /// <summary>Locates the ECH extension (type 0xfe0d) in a ClientHello and returns its enc, payload (ciphertext+tag), and the payload's byte offset in the buffer.</summary>
    private static (byte[] Enc, byte[] Payload, int PayloadOffset) ExtractEchExtension(byte[] clientHello)
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

        while (offset + 4 <= extensionsEnd)
        {
            var extType = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(offset));
            var extLen = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(offset + 2));
            var dataStart = offset + 4;

            if (extType == 0xfe0d)
            {
                var p = dataStart;
                p += 1; // ECH type byte (outer = 0)
                p += 4; // cipher_suite
                p += 1; // config_id
                var encLen = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(p));
                p += 2;
                var enc = clientHello[p..(p + encLen)];
                p += encLen;
                var payloadLen = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(p));
                p += 2;
                var payload = clientHello[p..(p + payloadLen)];
                return (enc, payload, p);
            }

            offset = dataStart + extLen;
        }

        throw new InvalidOperationException("ECH extension not found in outer ClientHello");
    }

    private static byte[] BuildClientHello(string sni, byte[] sessionId)
    {
        var sniExt = BuildSniExtension(sni);

        var body = new List<byte> { 0x03, 0x03 }; // client_version
        body.AddRange(new byte[32]); // random
        body.Add((byte)sessionId.Length);
        body.AddRange(sessionId);
        body.Add(0x00); body.Add(0x06); // cipher_suites length = 6 (3 suites)
        body.Add(0x13); body.Add(0x01); // TLS_AES_128_GCM_SHA256
        body.Add(0x13); body.Add(0x02); // TLS_AES_256_GCM_SHA384
        body.Add(0x13); body.Add(0x03); // TLS_CHACHA20_POLY1305_SHA256
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
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(hostname);
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
