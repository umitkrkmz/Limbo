using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Limbo.Core.Ech;

/// <summary>
/// Builds ECH-wrapped TLS ClientHello messages
/// </summary>
public class EchClientHelloBuilder
{
    private const ushort EchExtensionType = 0xfe0d; // Draft-13

    /// <summary>
    /// Wrap a ClientHello with ECH extension
    /// </summary>
    public static byte[]? WrapClientHello(
        ReadOnlySpan<byte> originalClientHello,
        EchConfig echConfig,
        string realSni)
    {
        if (echConfig.KeyConfig.PublicKey.Length == 0)
            return null;

        try
        {
            // Find SNI extension in original ClientHello and get its position
            var (sniOffset, sniLength) = FindSniExtension(originalClientHello);
            if (sniOffset < 0) return null;

            // Create HPKE context
            var hpke = new HpkeWrapper(echConfig.KeyConfig.PublicKey);

            // Build info for HPKE context
            var info = BuildHpkeInfo(echConfig);
            var enc = hpke.SetupSender(info);

            // Build ClientHelloInner (with real SNI)
            var clientHelloInner = BuildClientHelloInner(originalClientHello, realSni);

            // Encrypt ClientHelloInner
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);
            var aad = BuildAad(echConfig, enc);
            var encryptedInner = hpke.Seal(clientHelloInner, aad, nonce);

            // Build ECH extension payload
            var echExtension = BuildEchExtension(echConfig, enc, encryptedInner);

            // Build ClientHelloOuter with ECH extension and public_name as SNI
            var clientHelloOuter = BuildClientHelloOuter(
                originalClientHello, 
                echConfig.PublicName, 
                echExtension,
                sniOffset,
                sniLength);

            return clientHelloOuter;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Find SNI extension offset and length in ClientHello
    /// </summary>
    private static (int Offset, int Length) FindSniExtension(ReadOnlySpan<byte> clientHello)
    {
        // Skip TLS record header (5 bytes) + Handshake header (4 bytes)
        // + Version (2) + Random (32) + Session ID length (1 + var)
        // + Cipher suites (2 + var) + Compression (1 + var)
        // Then we're at extensions

        if (clientHello.Length < 43) return (-1, 0);

        var offset = 5 + 4 + 2 + 32; // Record + Handshake + Version + Random
        
        // Session ID
        var sessionIdLen = clientHello[offset];
        offset += 1 + sessionIdLen;

        // Cipher suites
        var cipherSuitesLen = BinaryPrimitives.ReadUInt16BigEndian(clientHello[offset..]);
        offset += 2 + cipherSuitesLen;

        // Compression methods
        var compressionLen = clientHello[offset];
        offset += 1 + compressionLen;

        if (offset + 2 > clientHello.Length) return (-1, 0);

        // Extensions length
        var extensionsLen = BinaryPrimitives.ReadUInt16BigEndian(clientHello[offset..]);
        offset += 2;

        var extensionsEnd = offset + extensionsLen;

        // Find SNI extension (type 0x0000)
        while (offset + 4 <= extensionsEnd)
        {
            var extType = BinaryPrimitives.ReadUInt16BigEndian(clientHello[offset..]);
            var extLen = BinaryPrimitives.ReadUInt16BigEndian(clientHello[(offset + 2)..]);

            if (extType == 0x0000) // SNI
            {
                return (offset, 4 + extLen);
            }

            offset += 4 + extLen;
        }

        return (-1, 0);
    }

    /// <summary>
    /// Build HPKE info parameter
    /// </summary>
    private static byte[] BuildHpkeInfo(EchConfig config)
    {
        // info = "tls ech" || 0x00 || ECHConfig
        var prefix = "tls ech\0"u8.ToArray();
        var result = new byte[prefix.Length + config.RawConfig.Length];
        prefix.CopyTo(result, 0);
        config.RawConfig.CopyTo(result, prefix.Length);
        return result;
    }

    /// <summary>
    /// Build ClientHelloInner with real SNI
    /// </summary>
    private static byte[] BuildClientHelloInner(ReadOnlySpan<byte> original, string realSni)
    {
        // For simplicity, just return encoded SNI for inner
        // In full implementation, this would be the complete inner ClientHello
        var sniBytes = Encoding.ASCII.GetBytes(realSni);
        var inner = new byte[sniBytes.Length + 16]; // Simplified
        sniBytes.CopyTo(inner, 0);
        return inner;
    }

    /// <summary>
    /// Build AAD for HPKE encryption
    /// </summary>
    private static byte[] BuildAad(EchConfig config, byte[] enc)
    {
        // aad = ClientHelloOuterAAD
        return config.RawConfig;
    }

    /// <summary>
    /// Build ECH extension
    /// </summary>
    private static byte[] BuildEchExtension(EchConfig config, byte[] enc, byte[] payload)
    {
        // ECH extension structure:
        // - type (1 byte): 0 for outer
        // - cipher_suite (4 bytes)
        // - config_id (1 byte)
        // - enc_len (2 bytes) + enc
        // - payload_len (2 bytes) + payload

        var suite = config.KeyConfig.CipherSuites.FirstOrDefault() ?? new HpkeCipherSuite();
        
        var extDataLen = 1 + 4 + 1 + 2 + enc.Length + 2 + payload.Length;
        var extLen = 4 + extDataLen; // type(2) + len(2) + data
        
        var result = new byte[extLen];
        var offset = 0;

        // Extension type
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), EchExtensionType);
        offset += 2;

        // Extension length
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)extDataLen);
        offset += 2;

        // Type (outer = 0)
        result[offset++] = 0;

        // Cipher suite
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), suite.KdfId);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), suite.AeadId);
        offset += 2;

        // Config ID
        result[offset++] = (byte)config.ConfigId;

        // Encapsulated key
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)enc.Length);
        offset += 2;
        enc.CopyTo(result, offset);
        offset += enc.Length;

        // Payload
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)payload.Length);
        offset += 2;
        payload.CopyTo(result, offset);

        return result;
    }

    /// <summary>
    /// Build ClientHelloOuter with ECH extension and public_name SNI
    /// </summary>
    private static byte[] BuildClientHelloOuter(
        ReadOnlySpan<byte> original,
        string publicName,
        byte[] echExtension,
        int sniOffset,
        int sniLength)
    {
        // Build new SNI extension with public_name
        var newSni = BuildSniExtension(publicName);

        // Calculate size difference
        var sizeDiff = newSni.Length - sniLength + echExtension.Length;

        // Create new ClientHello
        var result = new byte[original.Length + sizeDiff];
        var offset = 0;

        // Copy up to SNI extension
        original[..sniOffset].CopyTo(result);
        offset = sniOffset;

        // Insert new SNI
        newSni.CopyTo(result, offset);
        offset += newSni.Length;

        // Skip old SNI, copy rest
        var afterSni = sniOffset + sniLength;
        original[afterSni..].CopyTo(result.AsSpan(offset));
        offset += original.Length - afterSni;

        // Append ECH extension (before final bytes)
        // This is simplified - real implementation patches extension length
        echExtension.CopyTo(result, offset - echExtension.Length);

        // Update lengths in headers
        UpdateLengths(result, sizeDiff);

        return result;
    }

    /// <summary>
    /// Build SNI extension for public_name
    /// </summary>
    private static byte[] BuildSniExtension(string hostname)
    {
        var nameBytes = Encoding.ASCII.GetBytes(hostname);
        var totalLen = 2 + 2 + 2 + 1 + 2 + nameBytes.Length; // type + len + list_len + type + name_len + name
        var result = new byte[totalLen];

        var offset = 0;

        // Extension type (0x0000 = SNI)
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), 0x0000);
        offset += 2;

        // Extension data length
        var dataLen = 2 + 1 + 2 + nameBytes.Length;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)dataLen);
        offset += 2;

        // SNI list length
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)(1 + 2 + nameBytes.Length));
        offset += 2;

        // Name type (0 = hostname)
        result[offset++] = 0;

        // Name length
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)nameBytes.Length);
        offset += 2;

        // Name
        nameBytes.CopyTo(result, offset);

        return result;
    }

    /// <summary>
    /// Update TLS record and handshake message lengths
    /// </summary>
    private static void UpdateLengths(byte[] data, int sizeDiff)
    {
        // TLS record length (bytes 3-4)
        var recordLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(3));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), (ushort)(recordLen + sizeDiff));

        // Handshake message length (bytes 6-8, 3 bytes)
        var handshakeLen = (data[6] << 16) | (data[7] << 8) | data[8];
        handshakeLen += sizeDiff;
        data[6] = (byte)(handshakeLen >> 16);
        data[7] = (byte)(handshakeLen >> 8);
        data[8] = (byte)handshakeLen;
    }
}
