using System.Buffers.Text;
using System.Security.Cryptography;

namespace Rask.WebPush.Tests;

public sealed class VapidKeysTests
{
    [Fact]
    public void Generate_produces_a_65_byte_public_point_and_32_byte_scalar()
    {
        VapidKeys keys = VapidKeys.Generate();

        byte[] pub = Base64Url.DecodeFromChars(keys.PublicKey);
        byte[] priv = Base64Url.DecodeFromChars(keys.PrivateKey);
        Assert.Equal(65, pub.Length);
        Assert.Equal(0x04, pub[0]); // uncompressed point marker.
        Assert.Equal(32, priv.Length);
    }

    [Fact]
    public void Generate_keys_reimport_as_a_consistent_pair()
    {
        VapidKeys keys = VapidKeys.Generate();
        byte[] pub = Base64Url.DecodeFromChars(keys.PublicKey);
        byte[] priv = Base64Url.DecodeFromChars(keys.PrivateKey);

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] },
            D = priv
        };

        // Validate() throws if the public point and private scalar are inconsistent.
        parameters.Validate();
        using var ecdsa = ECDsa.Create(parameters);
        Assert.NotNull(ecdsa);
    }

    [Fact]
    public void Generate_public_key_is_clean_base64url()
    {
        VapidKeys keys = VapidKeys.Generate();
        Assert.DoesNotContain('+', keys.PublicKey);
        Assert.DoesNotContain('/', keys.PublicKey);
        Assert.DoesNotContain('=', keys.PublicKey);
    }

    [Fact]
    public void Generate_returns_distinct_pairs()
    {
        Assert.NotEqual(VapidKeys.Generate().PublicKey, VapidKeys.Generate().PublicKey);
    }
}
