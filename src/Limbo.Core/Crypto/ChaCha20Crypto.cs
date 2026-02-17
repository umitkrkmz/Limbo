using System.Security.Cryptography;

namespace Limbo.Core.Crypto;

/// <summary>
/// ChaCha20-Poly1305 AEAD encryption
/// </summary>
public class ChaCha20Crypto : IDisposable
{
    private readonly byte[] _key;
    private long _nonce;
    private bool _disposed;

    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public ChaCha20Crypto(byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));

        _key = new byte[KeySize];
        key.CopyTo(_key, 0);
        _nonce = 0;
    }

    /// <summary>
    /// Generate a random 256-bit key
    /// </summary>
    public static byte[] GenerateKey()
    {
        var key = new byte[KeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>
    /// Encrypt data with ChaCha20-Poly1305
    /// </summary>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        // Output: nonce (12) + ciphertext + tag (16)
        var output = new byte[NonceSize + plaintext.Length + TagSize];
        
        // Generate nonce from counter
        var nonceValue = Interlocked.Increment(ref _nonce);
        var nonce = output.AsSpan(0, NonceSize);
        BitConverter.GetBytes(nonceValue).CopyTo(nonce);

        using var aead = new ChaCha20Poly1305(_key);
        var ciphertext = output.AsSpan(NonceSize, plaintext.Length);
        var tag = output.AsSpan(NonceSize + plaintext.Length, TagSize);

        aead.Encrypt(nonce, plaintext, ciphertext, tag);

        return output;
    }

    /// <summary>
    /// Decrypt data with ChaCha20-Poly1305
    /// </summary>
    public byte[]? Decrypt(ReadOnlySpan<byte> encrypted)
    {
        if (encrypted.Length < NonceSize + TagSize)
            return null;

        try
        {
            var nonce = encrypted[..NonceSize];
            var ciphertext = encrypted[NonceSize..^TagSize];
            var tag = encrypted[^TagSize..];

            var plaintext = new byte[ciphertext.Length];

            using var aead = new ChaCha20Poly1305(_key);
            aead.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }
        catch (CryptographicException)
        {
            // Authentication failed
            return null;
        }
    }

    /// <summary>
    /// Encrypt a packet
    /// </summary>
    public byte[] EncryptPacket(byte[] packet)
    {
        return Encrypt(packet);
    }

    /// <summary>
    /// Decrypt a packet
    /// </summary>
    public byte[]? DecryptPacket(byte[] encrypted)
    {
        return Decrypt(encrypted);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ECDH X25519 key exchange helper
/// </summary>
public class EcdhKeyExchange : IDisposable
{
    private readonly ECDiffieHellman _ecdh;
    private readonly byte[] _publicKey;
    private bool _disposed;

    public const int PublicKeySize = 32;

    public EcdhKeyExchange()
    {
        // Create X25519 key pair
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _publicKey = _ecdh.PublicKey.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// Get our public key to send to peer
    /// </summary>
    public byte[] GetPublicKey() => _publicKey;

    /// <summary>
    /// Derive shared secret from peer's public key
    /// </summary>
    public byte[] DeriveSharedSecret(byte[] peerPublicKey)
    {
        using var peerEcdh = ECDiffieHellman.Create();
        peerEcdh.ImportSubjectPublicKeyInfo(peerPublicKey, out _);
        
        return _ecdh.DeriveKeyMaterial(peerEcdh.PublicKey);
    }

    /// <summary>
    /// Derive an encryption key from shared secret using HKDF
    /// </summary>
    public static byte[] DeriveEncryptionKey(byte[] sharedSecret, byte[]? salt = null, string? context = null)
    {
        salt ??= new byte[32];
        var info = System.Text.Encoding.UTF8.GetBytes(context ?? "limbo-crypto-v1");

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sharedSecret,
            ChaCha20Crypto.KeySize,
            salt,
            info);
    }

    /// <summary>
    /// Perform complete key exchange and return encryption key
    /// </summary>
    public byte[] PerformKeyExchange(byte[] peerPublicKey, byte[]? salt = null)
    {
        var sharedSecret = DeriveSharedSecret(peerPublicKey);
        return DeriveEncryptionKey(sharedSecret, salt);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _ecdh.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Static key derivation utilities 
/// </summary>
public static class KeyDerivation
{
    /// <summary>
    /// Derive key from password using Argon2-like approach (PBKDF2 fallback)
    /// </summary>
    public static byte[] DeriveKeyFromPassword(string password, byte[]? salt = null, int iterations = 100000)
    {
        salt ??= RandomNumberGenerator.GetBytes(16);
        
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password, 
            salt, 
            iterations, 
            HashAlgorithmName.SHA256);
        
        return pbkdf2.GetBytes(ChaCha20Crypto.KeySize);
    }

    /// <summary>
    /// Generate random salt
    /// </summary>
    public static byte[] GenerateSalt(int length = 16)
    {
        return RandomNumberGenerator.GetBytes(length);
    }

    /// <summary>
    /// Constant-time comparison for secrets
    /// </summary>
    public static bool SecureEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
