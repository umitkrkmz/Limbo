using System.Security.Cryptography;
using Limbo.Core.Ech;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;

namespace Limbo.Tests;

/// <summary>
/// Validates the HPKE primitives against the official RFC 9180 Appendix A.1.1
/// test vector: DHKEM(X25519, HKDF-SHA256), HKDF-SHA256, AES-128-GCM, mode_base
/// (0x00) - the ciphersuite Limbo's ECH bypass strategy uses.
/// </summary>
public class HpkeTests
{
    [Fact]
    public void MatchesRfc9180AppendixA1_1TestVector()
    {
        var info = Convert.FromHexString("4f6465206f6e2061204772656369616e2055726e");
        var skEm = Convert.FromHexString("52c4a758a802cd8b936eceea314432798d5baf2d7e9235dc084ab1b9cfa2f736");
        var pkEm = Convert.FromHexString("37fda3567bdbd628e88668c3c8d7e97d1d1253b6d4ea6d44c150f741f1bf4431");
        var pkRm = Convert.FromHexString("3948cfe0ad1ddb695d780e59077195da6c56506b027329794ab02bca80815c4d");

        var expectedSharedSecret = Convert.FromHexString("fe0e18c9f024ce43799ae393c7e8fe8fce9d218875e8227b0187c04e7d2ea1fc");
        var expectedKey = Convert.FromHexString("4531685d41d65f03dc48f6b8302c05b0");
        var expectedBaseNonce = Convert.FromHexString("56d890e5accaaf011cff4b7d");

        var plaintext = Convert.FromHexString("4265617574792069732074727574682c20747275746820626561757479");
        var aad = Convert.FromHexString("436f756e742d30");
        var expectedCiphertext = Convert.FromHexString(
            "f938558b5d72f1a23810b4be2ab4f84331acc02fc97babc53a52ae8218a355a96d8770ac83d07bea87e13c512a");

        // Encap: X25519(skEm, pkRm) -> dh, then DHKEM's ExtractAndExpand -> shared_secret
        var dh = new byte[32];
        var agreement = new X25519Agreement();
        agreement.Init(new X25519PrivateKeyParameters(skEm, 0));
        agreement.CalculateAgreement(new X25519PublicKeyParameters(pkRm, 0), dh, 0);

        var sharedSecret = HpkePrimitives.KemExtractAndExpand(dh, pkEm, pkRm);
        Assert.Equal(expectedSharedSecret, sharedSecret);

        // KeySchedule (mode_base)
        var (key, baseNonce, _) = HpkePrimitives.KeySchedule(sharedSecret, info);
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedBaseNonce, baseNonce);

        // Seal at sequence number 0 (nonce == base_nonce XOR 0 == base_nonce)
        var nonce = HpkePrimitives.ComputeNonce(baseNonce, 0);

        using var aes = new AesGcm(key, HpkePrimitives.Nt);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[HpkePrimitives.Nt];
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        var actualCiphertext = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(actualCiphertext, 0);
        tag.CopyTo(actualCiphertext, ciphertext.Length);

        Assert.Equal(expectedCiphertext, actualCiphertext);
    }

    [Fact]
    public void SetupSenderAndSealRoundTripsWithDeclaredNonceHandling()
    {
        // HpkeWrapper is sender-only (Limbo is an ECH client, not a server),
        // so this exercises the public API end-to-end and checks that two
        // Seal() calls under the same context never repeat a nonce - the
        // property that matters for AEAD safety.
        var recipientKey = new byte[32];
        RandomNumberGenerator.Fill(recipientKey);

        // Any 32 bytes work as an X25519 public key for this smoke test;
        // we only care that HpkeWrapper derives distinct nonces per call.
        var hpke = new HpkeWrapper(recipientKey);
        hpke.SetupSender("test-info"u8.ToArray());

        var ct1 = hpke.Seal("hello"u8.ToArray(), "aad"u8.ToArray());
        var ct2 = hpke.Seal("hello"u8.ToArray(), "aad"u8.ToArray());

        // Same plaintext/AAD but different (internally-incremented) nonces
        // must produce different ciphertexts.
        Assert.NotEqual(Convert.ToHexString(ct1), Convert.ToHexString(ct2));
    }
}
