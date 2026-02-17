using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Limbo.Core.Ech;

/// <summary>
/// HPKE (Hybrid Public Key Encryption) wrapper for ECH
/// Implements DHKEM(X25519, HKDF-SHA256, AES-128-GCM)
/// </summary>
public class HpkeWrapper
{
    private readonly byte[] _serverPublicKey;
    private byte[]? _sharedSecret;
    private byte[]? _encapsulatedKey;

    public HpkeWrapper(byte[] serverPublicKey)
    {
        _serverPublicKey = serverPublicKey;
    }

    /// <summary>
    /// Setup sender context and return encapsulated key
    /// </summary>
    public byte[] SetupSender(byte[] info)
    {
        // Generate ephemeral X25519 key pair
        var keyGen = new X25519KeyPairGenerator();
        keyGen.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = keyGen.GenerateKeyPair();

        var privateKey = (X25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (X25519PublicKeyParameters)keyPair.Public;

        // Encapsulated key is our public key
        _encapsulatedKey = publicKey.GetEncoded();

        // Compute shared secret via X25519 DH
        var serverPubKey = new X25519PublicKeyParameters(_serverPublicKey, 0);
        var agreement = new X25519Agreement();
        agreement.Init(privateKey);
        
        var secret = new byte[32];
        agreement.CalculateAgreement(serverPubKey, secret, 0);

        // Derive encryption key using HKDF-SHA256
        _sharedSecret = DeriveKey(secret, _encapsulatedKey, info);

        return _encapsulatedKey;
    }

    /// <summary>
    /// Encrypt data using AEAD (AES-128-GCM)
    /// </summary>
    public byte[] Seal(byte[] plaintext, byte[] aad, byte[] nonce)
    {
        if (_sharedSecret == null)
            throw new InvalidOperationException("Call SetupSender first");

        using var aes = new AesGcm(_sharedSecret.AsSpan(0, 16), 16);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        // Return ciphertext + tag
        var result = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(result, 0);
        tag.CopyTo(result, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Derive key using HKDF-SHA256
    /// </summary>
    private static byte[] DeriveKey(byte[] secret, byte[] enc, byte[] info)
    {
        // kem_context = enc || pkR
        // shared_secret = ExtractAndExpand(dh, kem_context)
        
        // Simplified HKDF: 
        // PRK = HMAC-SHA256(salt, IKM)
        // OKM = HMAC-SHA256(PRK, info || 0x01)

        using var hmac = new HMACSHA256(enc);
        var prk = hmac.ComputeHash(secret);

        using var hmac2 = new HMACSHA256(prk);
        var infoWithCounter = new byte[info.Length + 1];
        info.CopyTo(infoWithCounter, 0);
        infoWithCounter[^1] = 0x01;

        return hmac2.ComputeHash(infoWithCounter);
    }

    /// <summary>
    /// Get the encapsulated key (for including in ClientHello)
    /// </summary>
    public byte[] GetEncapsulatedKey() => 
        _encapsulatedKey ?? throw new InvalidOperationException("Call SetupSender first");
}
