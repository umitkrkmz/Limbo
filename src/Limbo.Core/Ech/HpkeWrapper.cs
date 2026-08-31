using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Limbo.Core.Ech;

/// <summary>
/// RFC 9180 HPKE (Hybrid Public Key Encryption) sender-side wrapper, mode_base (0x00).
/// Ciphersuite: DHKEM(X25519, HKDF-SHA256) + HKDF-SHA256 + AES-128-GCM
/// (kem_id=0x0020, kdf_id=0x0001, aead_id=0x0001) - the mandatory-to-implement
/// suite used by TLS Encrypted Client Hello.
/// </summary>
public class HpkeWrapper
{
    private readonly byte[] _serverPublicKey;
    private byte[]? _key;
    private byte[]? _baseNonce;
    private byte[]? _encapsulatedKey;
    private ulong _sequence;

    public HpkeWrapper(byte[] serverPublicKey)
    {
        _serverPublicKey = serverPublicKey;
    }

    /// <summary>
    /// Runs DHKEM(X25519, HKDF-SHA256) Encap against the recipient's public key,
    /// then the HPKE KeySchedule (mode_base). Returns "enc", the serialized
    /// ephemeral public key that must be sent alongside the ciphertext.
    /// </summary>
    public byte[] SetupSender(byte[] info)
    {
        var keyGen = new X25519KeyPairGenerator();
        keyGen.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = keyGen.GenerateKeyPair();

        var skE = (X25519PrivateKeyParameters)keyPair.Private;
        var pkE = (X25519PublicKeyParameters)keyPair.Public;

        var enc = pkE.GetEncoded();
        _encapsulatedKey = enc;

        var dh = new byte[HpkePrimitives.Ndh];
        var agreement = new X25519Agreement();
        agreement.Init(skE);
        agreement.CalculateAgreement(new X25519PublicKeyParameters(_serverPublicKey, 0), dh, 0);

        var sharedSecret = HpkePrimitives.KemExtractAndExpand(dh, enc, _serverPublicKey);
        var (key, baseNonce, _) = HpkePrimitives.KeySchedule(sharedSecret, info);

        _key = key;
        _baseNonce = baseNonce;
        _sequence = 0;

        return enc;
    }

    /// <summary>
    /// AEAD-seals plaintext under the key derived by <see cref="SetupSender"/>.
    /// The nonce is always computed internally as base_nonce XOR sequence per
    /// RFC 9180 Section 5.2 - a caller-supplied nonce would not be
    /// reproducible by a spec-conformant receiver, so there is no such
    /// parameter here.
    /// </summary>
    public byte[] Seal(byte[] plaintext, byte[] aad)
    {
        if (_key == null || _baseNonce == null)
            throw new InvalidOperationException("Call SetupSender first");

        var nonce = HpkePrimitives.ComputeNonce(_baseNonce, _sequence);
        _sequence++;

        using var aes = new AesGcm(_key, HpkePrimitives.Nt);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[HpkePrimitives.Nt];

        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        var result = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(result, 0);
        tag.CopyTo(result, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Get the encapsulated key (for including in ClientHello)
    /// </summary>
    public byte[] GetEncapsulatedKey() =>
        _encapsulatedKey ?? throw new InvalidOperationException("Call SetupSender first");
}
