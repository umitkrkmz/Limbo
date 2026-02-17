using System.Buffers.Binary;

namespace Limbo.Core.Ech;

/// <summary>
/// ECH (Encrypted Client Hello) configuration record
/// Parsed from DNS HTTPS record or server retry
/// </summary>
public record EchConfig
{
    public ushort Version { get; init; } = 0xfe0d; // Draft-13
    public byte ConfigId { get; init; }
    public HpkeKeyConfig KeyConfig { get; init; } = new();
    public byte MaximumNameLength { get; init; } = 64;
    public string PublicName { get; init; } = "";
    public byte[] RawConfig { get; init; } = [];

    /// <summary>
    /// Parse ECH config from DNS HTTPS record data
    /// </summary>
    public static EchConfig? Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.Length < 4) return null;

            var offset = 0;

            // Version (2 bytes)
            var version = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;

            // Length (2 bytes)
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;

            if (data.Length < offset + length) return null;

            // Config ID (1 byte)
            var configId = data[offset];
            offset += 1;

            // HPKE KEM ID (2 bytes)
            var kemId = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;

            // Public key length (2 bytes) + public key
            var pubKeyLen = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;

            var publicKey = data.Slice(offset, pubKeyLen).ToArray();
            offset += pubKeyLen;

            // Cipher suites length (2 bytes)
            var suitesLen = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;

            var cipherSuites = new List<HpkeCipherSuite>();
            var suitesEnd = offset + suitesLen;
            while (offset < suitesEnd)
            {
                var kdfId = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
                var aeadId = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
                cipherSuites.Add(new HpkeCipherSuite { KdfId = kdfId, AeadId = aeadId });
                offset += 4;
            }

            // Maximum name length (1 byte)
            var maxNameLen = data[offset];
            offset += 1;

            // Public name length (1 byte) + public name
            var pubNameLen = data[offset];
            offset += 1;

            var publicName = System.Text.Encoding.ASCII.GetString(data.Slice(offset, pubNameLen));
            offset += pubNameLen;

            return new EchConfig
            {
                Version = version,
                ConfigId = configId,
                KeyConfig = new HpkeKeyConfig
                {
                    KemId = kemId,
                    PublicKey = publicKey,
                    CipherSuites = cipherSuites
                },
                MaximumNameLength = maxNameLen,
                PublicName = publicName,
                RawConfig = data[..(4 + length)].ToArray()
            };
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// HPKE key configuration
/// </summary>
public class HpkeKeyConfig
{
    public ushort KemId { get; init; } = 0x0020; // DHKEM(X25519)
    public byte[] PublicKey { get; init; } = [];
    public List<HpkeCipherSuite> CipherSuites { get; init; } = [];
}

/// <summary>
/// HPKE cipher suite
/// </summary>
public class HpkeCipherSuite
{
    public ushort KdfId { get; init; } = 0x0001; // HKDF-SHA256
    public ushort AeadId { get; init; } = 0x0001; // AES-128-GCM
}
