using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Rask.WebPush;

// RFC 8291 "Message Encryption for Web Push" over the RFC 8188 aes128gcm content coding. Produces the
// full request body for a single record: a content-coding header followed by one AES-128-GCM record.
internal static class Aes128GcmEncryptor
{
    private const int RecordSize = 4096; // `rs`; one notification payload fits comfortably under this.

    private static readonly byte[] KeyInfoPrefix = "WebPush: info\0"u8.ToArray();
    private static readonly byte[] CekInfo = "Content-Encoding: aes128gcm\0"u8.ToArray();
    private static readonly byte[] NonceInfo = "Content-Encoding: nonce\0"u8.ToArray();

    // Encrypt `plaintext` for the subscription's keys. `uaPublicKey` is the raw 65-byte uncompressed
    // P-256 point (decoded p256dh); `authSecret` is the 16-byte decoded auth value.
    public static byte[] Encrypt(ReadOnlySpan<byte> uaPublicKey, ReadOnlySpan<byte> authSecret, ReadOnlySpan<byte> plaintext)
    {
        if (uaPublicKey.Length != 65 || uaPublicKey[0] != 0x04)
            throw new ArgumentException("Subscription p256dh must be a 65-byte uncompressed P-256 point.", nameof(uaPublicKey));
        if (authSecret.Length != 16) // RFC 8291 §3.1 fixes the auth secret at 16 bytes.
            throw new ArgumentException("Subscription auth secret must be 16 bytes.", nameof(authSecret));
        if (plaintext.Length > RecordSize - 17) // 16-byte GCM tag + 1-byte record delimiter.
            throw new ArgumentException($"Payload too large for a single record (max {RecordSize - 17} bytes).", nameof(plaintext));

        // 1. Ephemeral server key pair; export its public point as the 65-byte `as_public`.
        using var serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ECParameters sp = serverEcdh.ExportParameters(includePrivateParameters: false);
        var asPublic = new byte[65];
        asPublic[0] = 0x04;
        VapidKeys.CopyRightAligned(sp.Q.X!, asPublic, 1, 32);
        VapidKeys.CopyRightAligned(sp.Q.Y!, asPublic, 33, 32);

        // 2. Raw ECDH shared secret (32-byte X coordinate). DeriveRawSecretAgreement gives the bytes
        //    RFC 8291 wants — DeriveKeyFromHash/DeriveKeyMaterial would hash them and break decryption.
        using var ua = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uaPublicKey[1..33].ToArray(), Y = uaPublicKey[33..65].ToArray() }
        });
        byte[] ecdhSecret = serverEcdh.DeriveRawSecretAgreement(ua.PublicKey);

        // 3. Random 16-byte salt.
        byte[] salt = RandomNumberGenerator.GetBytes(16);

        // 4. HKDF-SHA256 chain (RFC 8291 §3.4). Compute the IKM-PRK once, then derive CEK and nonce.
        byte[] prkKey = HKDF.Extract(HashAlgorithmName.SHA256, ecdhSecret, authSecret.ToArray());
        byte[] keyInfo = Concat(KeyInfoPrefix, uaPublicKey, asPublic); // recipient (UA) then sender (AS).
        byte[] ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);

        byte[] prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        byte[] cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, CekInfo);
        byte[] nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, NonceInfo);

        // 5. Record = plaintext ‖ 0x02 (the delimiter for the last — and here only — record), then GCM.
        var record = new byte[plaintext.Length + 1];
        plaintext.CopyTo(record);
        record[plaintext.Length] = 0x02;

        var ciphertext = new byte[record.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, tag.Length))
            aes.Encrypt(nonce, record, ciphertext, tag);

        // 6. Body = salt(16) ‖ rs(4, big-endian) ‖ idlen(1)=65 ‖ as_public(65) ‖ ciphertext ‖ tag(16).
        var body = new byte[16 + 4 + 1 + 65 + ciphertext.Length + tag.Length];
        var span = body.AsSpan();
        salt.CopyTo(span);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(16, 4), RecordSize);
        span[20] = 65;
        asPublic.CopyTo(span.Slice(21, 65));
        ciphertext.CopyTo(span.Slice(86, ciphertext.Length));
        tag.CopyTo(span.Slice(86 + ciphertext.Length, tag.Length));
        return body;
    }

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
