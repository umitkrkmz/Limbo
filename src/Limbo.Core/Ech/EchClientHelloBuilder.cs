using System.Buffers.Binary;
using System.Text;
using Limbo.Core.Detection;

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
            var clientHelloInner = BuildClientHelloInner(originalClientHello, echConfig, realSni);

            // AAD is the "ClientHelloOuterAAD": the ClientHelloOuter we're about to
            // send, but with the ECH extension's payload zeroed out (same length as
            // the real ciphertext will be, since AES-128-GCM is length-preserving
            // plus a fixed-size tag). Build it now, before we have the real
            // ciphertext, so HPKE can bind the encryption to the exact outer message.
            var aad = BuildOuterAad(originalClientHello, echConfig, enc, clientHelloInner.Length, sniOffset, sniLength);
            var encryptedInner = hpke.Seal(clientHelloInner, aad);

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

    private static int FindExtensionsLengthOffset(ReadOnlySpan<byte> clientHello) =>
        TlsClientHello.FindExtensionsLengthOffset(clientHello);

    private static (int Offset, int Length) FindSniExtension(ReadOnlySpan<byte> clientHello) =>
        TlsClientHello.FindExtension(clientHello, 0x0000);

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
    /// Build the EncodedClientHelloInner (draft-ietf-tls-esni §5.1/§6.1.3):
    /// the real client's ClientHello body, byte-for-byte, with only
    /// legacy_session_id zeroed and padding appended.
    ///
    /// This deliberately does NOT synthesize a new ClientHello (new random,
    /// new key_share, etc.). Limbo intercepts and rewrites a real client's
    /// outbound packet - the eventual TLS 1.3 session keys are derived from
    /// this exact content (in particular the client's key_share, whose
    /// private key only the real client holds). Substituting our own would
    /// desync the real client's TLS stack from what the server computes and
    /// silently break the connection. The server reconstructs
    /// legacy_session_id from ClientHelloOuter's (unencrypted) copy, so
    /// zeroing it here is the one required transformation.
    /// </summary>
    private static byte[] BuildClientHelloInner(ReadOnlySpan<byte> original, EchConfig echConfig, string realSni)
    {
        const int headerLen = 5 + 4; // record header + handshake header
        var body = original[headerLen..];

        const int sessionIdLenOffset = 2 + 32; // client_version(2) + random(32)
        var sessionIdLen = body[sessionIdLenOffset];
        var afterSessionId = sessionIdLenOffset + 1 + sessionIdLen;

        var encoded = new byte[sessionIdLenOffset + 1 + (body.Length - afterSessionId)];
        body[..sessionIdLenOffset].CopyTo(encoded);
        encoded[sessionIdLenOffset] = 0x00; // legacy_session_id: empty
        body[afterSessionId..].CopyTo(encoded.AsSpan(sessionIdLenOffset + 1));

        return PadEncodedClientHelloInner(encoded, echConfig.MaximumNameLength, realSni.Length);
    }

    /// <summary>
    /// Pads EncodedClientHelloInner per §6.1.3's recommended scheme: enough
    /// bytes to make the real SNI's length indistinguishable from
    /// ECHConfig's advertised maximum_name_length, then rounded up to a
    /// multiple of 32 bytes so the ciphertext length alone doesn't leak
    /// fine-grained information about the real hostname.
    /// </summary>
    private static byte[] PadEncodedClientHelloInner(byte[] encoded, int maximumNameLength, int sniLength)
    {
        var sniPadding = Math.Max(0, maximumNameLength - sniLength);
        var lengthAfterSniPadding = encoded.Length + sniPadding;
        var blockPadding = 31 - ((lengthAfterSniPadding - 1) % 32);

        var padded = new byte[encoded.Length + sniPadding + blockPadding]; // trailing bytes already zero
        encoded.CopyTo(padded, 0);
        return padded;
    }

    /// <summary>
    /// Build the ClientHelloOuterAAD: the fully-formed ClientHelloOuter we're
    /// about to send, with the ECH extension's payload zeroed out. This lets
    /// HPKE's AEAD bind the encrypted inner hello to the exact outer message
    /// it will travel with, without a circular dependency on the ciphertext
    /// itself (the ciphertext has the same length as the plaintext plus a
    /// fixed-size AEAD tag, so the placeholder is already the right size).
    /// </summary>
    private static byte[] BuildOuterAad(
        ReadOnlySpan<byte> original,
        EchConfig config,
        byte[] enc,
        int innerPlaintextLength,
        int sniOffset,
        int sniLength)
    {
        var placeholderPayload = new byte[innerPlaintextLength + HpkePrimitives.Nt];
        var placeholderExtension = BuildEchExtension(config, enc, placeholderPayload);
        return BuildClientHelloOuter(original, config.PublicName, placeholderExtension, sniOffset, sniLength);
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

        // Append ECH extension. Extensions are the last field of ClientHello,
        // so `offset` now points exactly at the end of the copied data -
        // appending here places it as the final extension in the list.
        echExtension.CopyTo(result, offset);

        // Update lengths in headers (record, handshake, and extensions-list length)
        var extensionsLengthOffset = FindExtensionsLengthOffset(original);
        UpdateLengths(result, sizeDiff, extensionsLengthOffset);

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
    /// Update TLS record, handshake message, and extensions-list lengths
    /// </summary>
    private static void UpdateLengths(byte[] data, int sizeDiff, int extensionsLengthOffset)
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

        // Extensions list length (2 bytes)
        var extensionsLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(extensionsLengthOffset));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(extensionsLengthOffset), (ushort)(extensionsLen + sizeDiff));
    }
}
