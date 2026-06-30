using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.WebPush;

// VAPID (Voluntary Application Server Identification, RFC 8292): builds the signed ES256 JWT and the
// "vapid t=…,k=…" Authorization header value that authenticate the push request to the service.
internal static partial class Vapid
{
    // {"typ":"JWT","alg":"ES256"} — constant, so encode it once.
    private static readonly string HeaderSegment = Base64Url.Encode("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"u8);

    // Build the Authorization header value for a request to `endpoint`. `expires` is the token's
    // expiry (must be ≤ now + 24h per the spec; the sender uses now + 12h).
    public static string BuildAuthorizationHeader(string endpoint, VapidKeys keys, string subject, DateTimeOffset expires)
    {
        string jwt = BuildSignedJwt(endpoint, keys, subject, expires);
        return $"vapid t={jwt},k={keys.PublicKey}";
    }

    // The signed JWT on its own (header.payload.signature), exposed for testing.
    public static string BuildSignedJwt(string endpoint, VapidKeys keys, string subject, DateTimeOffset expires)
    {
        var claims = new VapidClaims
        {
            Audience = Audience(endpoint),
            Expiration = expires.ToUnixTimeSeconds(),
            Subject = subject
        };

        string payloadSegment = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(claims, VapidJsonContext.Default.VapidClaims));
        string signingInput = $"{HeaderSegment}.{payloadSegment}";

        using ECDsa ecdsa = CreateEcdsa(keys);
        // ES256 requires the raw R‖S signature (IEEE P1363, 64 bytes) — the default SignData overload
        // emits DER, which is invalid for JWS.
        byte[] signature = ecdsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{signingInput}.{Base64Url.Encode(signature)}";
    }

    // The `aud` claim is the origin of the endpoint — scheme + host (+ explicit port) with NO path.
    // A trailing path or slash here causes the push service to reject the token (401).
    public static string Audience(string endpoint) => new Uri(endpoint).GetLeftPart(UriPartial.Authority);

    // Import the base64url VAPID key pair into an ECDsa over P-256. Both the private scalar D and the
    // public point Q are supplied so ECParameters.Validate() can confirm they are consistent.
    public static ECDsa CreateEcdsa(VapidKeys keys)
    {
        byte[] pub = Base64Url.Decode(keys.PublicKey);
        byte[] d = Base64Url.Decode(keys.PrivateKey);

        if (pub.Length != 65 || pub[0] != 0x04)
            throw new ArgumentException("VAPID public key must be a 65-byte uncompressed P-256 point (0x04 ‖ X ‖ Y).", nameof(keys));
        if (d.Length != 32)
            throw new ArgumentException("VAPID private key must be the 32-byte P-256 scalar.", nameof(keys));

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] },
            D = d
        };
        parameters.Validate();
        return ECDsa.Create(parameters);
    }

    private sealed class VapidClaims
    {
        [JsonPropertyName("aud")] public string Audience { get; init; } = "";
        [JsonPropertyName("exp")] public long Expiration { get; init; }
        [JsonPropertyName("sub")] public string Subject { get; init; } = "";
    }

    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(VapidClaims))]
    private sealed partial class VapidJsonContext : JsonSerializerContext;
}
