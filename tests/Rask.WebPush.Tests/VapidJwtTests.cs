using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rask.WebPush.Tests;

// Verifies the VAPID Authorization header (RFC 8292) the sender attaches: the ES256 JWT must verify
// against the public key, and its claims must satisfy what push services require.
public sealed class VapidJwtTests
{
    private static async Task<string> CaptureAuthorization(WebPushOptions options)
    {
        var handler = new RecordingHandler();
        using var client = TestCrypto.GenerateClient();
        var sub = new PushSubscription(TestSender.Endpoint, client.P256dhB64, client.AuthB64);
        await TestSender.Create(handler, options).SendAsync(sub, WebPushMessage.Text("hi"));
        return handler.Request!.Headers.GetValues("Authorization").Single();
    }

    [Fact]
    public async Task Header_is_vapid_scheme_with_t_and_k()
    {
        var options = TestSender.Options();
        string header = await CaptureAuthorization(options);

        Assert.StartsWith("vapid t=", header, StringComparison.Ordinal);
        (string jwt, string k) = ParseHeader(header);
        Assert.Equal(options.VapidKeys!.PublicKey, k);
        Assert.Equal(65, Base64Url.DecodeFromChars(k).Length); // uncompressed P-256 point.
        Assert.Equal(3, jwt.Split('.').Length);
    }

    [Fact]
    public async Task Jwt_signature_verifies_against_the_public_key()
    {
        var options = TestSender.Options();
        string header = await CaptureAuthorization(options);
        (string jwt, _) = ParseHeader(header);
        string[] parts = jwt.Split('.');

        using ECDsa ecdsa = ImportPublic(options.VapidKeys!.PublicKey);
        byte[] signature = Base64Url.DecodeFromChars(parts[2]);
        bool ok = ecdsa.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.True(ok);
        Assert.Equal(64, signature.Length); // raw R‖S, not DER.
    }

    [Fact]
    public async Task Claims_have_origin_only_aud_subject_and_bounded_exp()
    {
        var options = TestSender.Options();
        string header = await CaptureAuthorization(options);
        (string jwt, _) = ParseHeader(header);

        using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Split('.')[1]));
        JsonElement claims = doc.RootElement;

        // aud is the endpoint's origin with no path/trailing slash.
        Assert.Equal("https://fcm.googleapis.com", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:admin@example.com", claims.GetProperty("sub").GetString());

        long exp = claims.GetProperty("exp").GetInt64();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(exp, now, now + (24 * 60 * 60)); // must not exceed now + 24h.
    }

    private static (string Jwt, string Key) ParseHeader(string header)
    {
        string value = header["vapid ".Length..];
        string[] parts = value.Split(",k=");
        return (parts[0]["t=".Length..], parts[1]);
    }

    private static ECDsa ImportPublic(string publicKeyB64)
    {
        byte[] pub = Base64Url.DecodeFromChars(publicKeyB64);
        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] }
        });
    }
}
