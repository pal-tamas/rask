using System.Formats.Cbor;
using System.Security.Cryptography;

namespace Rask.Auth;

/// <summary>
/// A credential public key, as an authenticator encoded it: a COSE_Key (RFC 8152) holding an ES256 or RS256 key.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what Rask needs from COSE. Two algorithms are accepted, and both are ones browsers actually
/// produce: ES256 (ECDSA over P-256, what every platform authenticator uses) and RS256 (what some Windows Hello TPM
/// keys use). Anything else is refused at registration, so a stored key is always one of the two.
/// </para>
/// <para>
/// The reader is strict and bounded. Indefinite-length maps, duplicate labels and non-shortest encodings are refused
/// by <see cref="CborConformanceMode.Strict" />, and every length is checked before a key is built, so a hostile blob
/// fails closed rather than allocating on demand.
/// </para>
/// </remarks>
internal sealed class CoseKey : IDisposable
{
    /// <summary>ECDSA over P-256 with SHA-256.</summary>
    internal const int ES256 = -7;

    /// <summary>RSASSA-PKCS1-v1_5 with SHA-256.</summary>
    internal const int RS256 = -257;

    // COSE key types (RFC 8152 §13).
    private const int KeyTypeEc2 = 2;
    private const int KeyTypeRsa = 3;

    // COSE_Key labels. Positive ones are common; negative ones are key-type specific, so -1 is the curve for an EC2
    // key and the modulus for an RSA one.
    private const int LabelKeyType = 1;
    private const int LabelAlgorithm = 3;
    private const int LabelCurveOrModulus = -1;
    private const int LabelXOrExponent = -2;
    private const int LabelY = -3;

    private const int CurveP256 = 1;
    private const int P256CoordinateLength = 32;

    // 2048 bits up to 8192, which covers every RSA key a real authenticator produces and is a bound a garbage blob
    // cannot pass.
    private const int MinRsaModulusLength = 256;
    private const int MaxRsaModulusLength = 1024;
    private const int MaxRsaExponentLength = 8;

    private readonly AsymmetricAlgorithm _key;

    private CoseKey(int algorithm, AsymmetricAlgorithm key)
    {
        Algorithm = algorithm;
        _key = key;
    }

    /// <summary>The COSE algorithm this key signs with: <see cref="ES256" /> or <see cref="RS256" />.</summary>
    public int Algorithm { get; }

    /// <summary>Reads a COSE_Key, returning <see langword="null" /> for anything malformed or unsupported.</summary>
    /// <param name="cose">The encoded key.</param>
    /// <param name="bytesRead">How much of <paramref name="cose" /> the key occupied, for a key with data after it.</param>
    public static CoseKey? Read(ReadOnlyMemory<byte> cose, out int bytesRead)
    {
        bytesRead = 0;

        try
        {
            var reader = new CborReader(cose, CborConformanceMode.Strict);
            var key = ReadKey(reader);
            bytesRead = cose.Length - reader.BytesRemaining;
            return key;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException
                                              or CryptographicException or OverflowException or ArgumentException)
        {
            // A malformed key is an invalid credential, never an exception the caller has to think about.
            return null;
        }
    }

    /// <summary>Whether <paramref name="signature" /> is this key's signature over <paramref name="data" />.</summary>
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        try
        {
            return Algorithm switch
            {
                // WebAuthn's ES256 signature is the ASN.1 DER sequence an authenticator emits, not a raw r||s pair.
                ES256 => ((ECDsa)_key).VerifyData(
                    data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
                _ => ((RSA)_key).VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            };
        }
        catch (CryptographicException)
        {
            // A signature that is not even well-formed is a failed verification like any other.
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _key.Dispose();

    private static CoseKey? ReadKey(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.StartMap || reader.ReadStartMap() is not { } count)
        {
            // Definite-length map or nothing: an indefinite one is not canonical CBOR and no authenticator writes it.
            return null;
        }

        int? keyType = null;
        int? algorithm = null;
        int? curve = null;
        byte[]? modulus = null;
        byte[]? x = null;
        byte[]? y = null;
        byte[]? exponent = null;

        for (var i = 0; i < count; i++)
        {
            if (reader.PeekState() is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
            {
                // Every COSE_Key label Rask reads is an integer; a text label belongs to an extension it does not
                // support.
                return null;
            }

            switch (reader.ReadInt32())
            {
                case LabelKeyType:
                    keyType = ReadInteger(reader);
                    break;
                case LabelAlgorithm:
                    algorithm = ReadInteger(reader);
                    break;
                case LabelCurveOrModulus when reader.PeekState() == CborReaderState.ByteString:
                    modulus = reader.ReadByteString();
                    break;
                case LabelCurveOrModulus:
                    curve = ReadInteger(reader);
                    break;
                case LabelXOrExponent:
                    // x for an EC2 key, e for an RSA one — which it is follows from the key type.
                    x = exponent = ReadByteString(reader);
                    break;
                case LabelY:
                    y = ReadByteString(reader);
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndMap();

        return keyType switch
        {
            KeyTypeEc2 => ReadEc2(algorithm, curve, x, y),
            KeyTypeRsa => ReadRsa(algorithm, modulus, exponent),
            _ => null,
        };
    }

    private static CoseKey? ReadEc2(int? algorithm, int? curve, byte[]? x, byte[]? y)
    {
        if (algorithm != ES256
            || curve != CurveP256
            || x is not { Length: P256CoordinateLength }
            || y is not { Length: P256CoordinateLength })
        {
            return null;
        }

        // Throws for a point that is not on the curve, which Read turns into a null key.
        var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y },
        });

        return new CoseKey(ES256, ecdsa);
    }

    private static CoseKey? ReadRsa(int? algorithm, byte[]? modulus, byte[]? exponent)
    {
        if (algorithm != RS256
            || modulus is not { Length: >= MinRsaModulusLength and <= MaxRsaModulusLength }
            || exponent is not { Length: > 0 and <= MaxRsaExponentLength })
        {
            return null;
        }

        var rsa = RSA.Create(new RSAParameters { Modulus = modulus, Exponent = exponent });
        return new CoseKey(RS256, rsa);
    }

    private static int? ReadInteger(CborReader reader) =>
        reader.PeekState() is CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger
            ? reader.ReadInt32()
            : null;

    private static byte[]? ReadByteString(CborReader reader) =>
        reader.PeekState() == CborReaderState.ByteString ? reader.ReadByteString() : null;
}
