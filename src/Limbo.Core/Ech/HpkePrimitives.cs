using System.Security.Cryptography;
using System.Text;

namespace Limbo.Core.Ech;

/// <summary>
/// Pure RFC 9180 HPKE building blocks for the mandatory-to-implement ECH
/// ciphersuite: DHKEM(X25519, HKDF-SHA256) + HKDF-SHA256 + AES-128-GCM,
/// mode_base (0x00, no PSK, no sender authentication).
/// Kept separate from <see cref="HpkeWrapper"/> (which owns X25519 key
/// generation via BouncyCastle) so these deterministic steps can be
/// exercised directly against the RFC 9180 Appendix A.1.1 test vector.
/// </summary>
internal static class HpkePrimitives
{
    public const ushort KemId = 0x0020;  // DHKEM(X25519, HKDF-SHA256)
    public const ushort KdfId = 0x0001;  // HKDF-SHA256
    public const ushort AeadId = 0x0001; // AES-128-GCM

    public const int Ndh = 32;     // X25519 DH output size
    public const int Nsecret = 32; // DHKEM shared secret size (= HKDF-SHA256 output size)
    public const int Nk = 16;      // AES-128 key size
    public const int Nn = 12;      // AES-GCM nonce size
    public const int Nt = 16;      // AES-GCM tag size
    public const int Nh = 32;      // HKDF-SHA256 hash size (exporter_secret size)

    private static readonly byte[] Hpkev1 = "HPKE-v1"u8.ToArray();
    private static readonly byte[] KemSuiteId = Concat("KEM"u8.ToArray(), I2Osp(KemId, 2));
    private static readonly byte[] HpkeSuiteId = Concat(
        "HPKE"u8.ToArray(), I2Osp(KemId, 2), I2Osp(KdfId, 2), I2Osp(AeadId, 2));

    /// <summary>
    /// DHKEM's ExtractAndExpand: turns a raw X25519 Diffie-Hellman output into
    /// the KEM shared secret. <paramref name="enc"/> is the sender's serialized
    /// ephemeral public key, <paramref name="pkRm"/> the recipient's public key.
    /// </summary>
    public static byte[] KemExtractAndExpand(byte[] dh, byte[] enc, byte[] pkRm)
    {
        var kemContext = Concat(enc, pkRm);
        var eaePrk = LabeledExtract(KemSuiteId, Array.Empty<byte>(), "eae_prk", dh);
        return LabeledExpand(KemSuiteId, eaePrk, "shared_secret", kemContext, Nsecret);
    }

    /// <summary>
    /// HPKE KeySchedule for mode_base: derives (key, base_nonce, exporter_secret)
    /// from the KEM shared secret and the caller-supplied HPKE "info".
    /// </summary>
    public static (byte[] Key, byte[] BaseNonce, byte[] ExporterSecret) KeySchedule(byte[] sharedSecret, byte[] info)
    {
        const byte modeBase = 0x00;
        var psk = Array.Empty<byte>();
        var pskId = Array.Empty<byte>();

        var pskIdHash = LabeledExtract(HpkeSuiteId, Array.Empty<byte>(), "psk_id_hash", pskId);
        var infoHash = LabeledExtract(HpkeSuiteId, Array.Empty<byte>(), "info_hash", info);
        var keyScheduleContext = Concat(new[] { modeBase }, pskIdHash, infoHash);

        var secret = LabeledExtract(HpkeSuiteId, sharedSecret, "secret", psk);

        var key = LabeledExpand(HpkeSuiteId, secret, "key", keyScheduleContext, Nk);
        var baseNonce = LabeledExpand(HpkeSuiteId, secret, "base_nonce", keyScheduleContext, Nn);
        var exporterSecret = LabeledExpand(HpkeSuiteId, secret, "exp", keyScheduleContext, Nh);

        return (key, baseNonce, exporterSecret);
    }

    /// <summary>Per-message nonce: base_nonce XOR I2OSP(sequence, Nn).</summary>
    public static byte[] ComputeNonce(byte[] baseNonce, ulong sequence)
    {
        var seqBytes = I2Osp(sequence, Nn);
        var nonce = new byte[Nn];
        for (var i = 0; i < Nn; i++)
            nonce[i] = (byte)(baseNonce[i] ^ seqBytes[i]);
        return nonce;
    }

    private static byte[] LabeledExtract(byte[] suiteId, byte[] salt, string label, byte[] ikm)
    {
        var labeledIkm = Concat(Hpkev1, suiteId, Encoding.ASCII.GetBytes(label), ikm);
        return HKDF.Extract(HashAlgorithmName.SHA256, labeledIkm, salt);
    }

    private static byte[] LabeledExpand(byte[] suiteId, byte[] prk, string label, byte[] info, int length)
    {
        var labeledInfo = Concat(I2Osp((ulong)length, 2), Hpkev1, suiteId, Encoding.ASCII.GetBytes(label), info);
        return HKDF.Expand(HashAlgorithmName.SHA256, prk, length, labeledInfo);
    }

    private static byte[] I2Osp(ulong value, int length)
    {
        var result = new byte[length];
        for (var i = length - 1; i >= 0; i--)
        {
            result[i] = (byte)(value & 0xFF);
            value >>= 8;
        }
        return result;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}
