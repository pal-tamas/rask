using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace Rask.WebPush.Tests;

// Test-only counterpart to the production crypto: it stands in for the browser (user agent), holding
// the P-256 private key + auth secret and DECRYPTING what WebPushSender produced. A successful
// decrypt that yields the expected plaintext is end-to-end proof of RFC 8291 correctness without a
// live browser or push service.
internal static class TestCrypto
{
    // A simulated browser subscription: the private side stays in the test, the public side
    // (p256dh/auth, base64url) is handed to the sender as a PushSubscription.
    public sealed class Client : IDisposable
    {
        public required ECDiffieHellman Ecdh { get; init; }
        public required byte[] P256dh { get; init; } // 65-byte uncompressed public point.
        public required byte[] Auth { get; init; }   // 16-byte auth secret.

        public string P256dhB64 => Base64Url.EncodeToString(P256dh);
        public string AuthB64 => Base64Url.EncodeToString(Auth);

        public void Dispose() => Ecdh.Dispose();
    }

    public static Client GenerateClient()
    {
        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ECParameters p = ecdh.ExportParameters(includePrivateParameters: false);

        var pub = new byte[65];
        pub[0] = 0x04;
        CopyRightAligned(p.Q.X!, pub, 1, 32);
        CopyRightAligned(p.Q.Y!, pub, 33, 32);

        return new Client { Ecdh = ecdh, P256dh = pub, Auth = RandomNumberGenerator.GetBytes(16) };
    }

    // Reverse of Aes128GcmEncryptor.Encrypt: parse the aes128gcm body, run the same HKDF chain with
    // the client's private key, and AES-128-GCM-decrypt the single record.
    public static byte[] Decrypt(byte[] body, Client client)
    {
        var span = body.AsSpan();
        byte[] salt = span.Slice(0, 16).ToArray();
        uint rs = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(16, 4));
        Assert.Equal(4096u, rs);
        int idlen = span[20];
        Assert.Equal(65, idlen);
        byte[] asPublic = span.Slice(21, idlen).ToArray();
        byte[] cipherAndTag = span.Slice(21 + idlen).ToArray();

        using var server = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = asPublic[1..33], Y = asPublic[33..65] }
        });
        byte[] ecdhSecret = client.Ecdh.DeriveRawSecretAgreement(server.PublicKey);

        byte[] prkKey = HKDF.Extract(HashAlgorithmName.SHA256, ecdhSecret, client.Auth);
        byte[] keyInfo = Concat("WebPush: info\0"u8, client.P256dh, asPublic);
        byte[] ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);
        byte[] prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        byte[] cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        byte[] nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        byte[] tag = cipherAndTag[^16..];
        byte[] ciphertext = cipherAndTag[..^16];
        var record = new byte[ciphertext.Length];
        using (var aes = new AesGcm(cek, tag.Length))
            aes.Decrypt(nonce, ciphertext, tag, record);

        // Strip the trailing record delimiter (0x02 for the last/only record).
        int end = record.Length - 1;
        Assert.Equal(0x02, record[end]);
        return record[..end];
    }

    private static void CopyRightAligned(ReadOnlySpan<byte> source, Span<byte> destination, int offset, int width) =>
        source.CopyTo(destination.Slice(offset + width - source.Length, source.Length));

    private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c)
    {
        var result = new byte[a.Length + b.Length + c.Length];
        var span = result.AsSpan();
        a.CopyTo(span);
        b.CopyTo(span.Slice(a.Length));
        c.CopyTo(span.Slice(a.Length + b.Length));
        return result;
    }
}
