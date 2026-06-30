using System.Security.Cryptography;

namespace Rask.WebPush;

// A VAPID application-server key pair (RFC 8292), both members base64url-encoded:
//   PublicKey  — the uncompressed P-256 point (65 bytes: 0x04 ‖ X ‖ Y). This is exactly the
//                `applicationServerKey` the browser passes to pushManager.subscribe, so hand the
//                SAME string to the client's IWebPush.SubscribeAsync.
//   PrivateKey — the 32-byte private scalar D. Keep it secret on the server.
//
// Generate one pair per application with Generate(), store it in configuration/secrets, and reuse it
// for the lifetime of the app — rotating it invalidates every existing subscription.
public sealed record VapidKeys(string PublicKey, string PrivateKey)
{
    // Create a fresh P-256 key pair in the exact base64url form the browser and this sender expect.
    public static VapidKeys Generate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters p = ecdsa.ExportParameters(includePrivateParameters: true);

        // 0x04 ‖ X(32) ‖ Y(32). ExportParameters may return coordinates shorter than 32 bytes when
        // the high byte is zero, so right-align each into its fixed-width slot.
        var pub = new byte[65];
        pub[0] = 0x04;
        CopyRightAligned(p.Q.X!, pub, 1, 32);
        CopyRightAligned(p.Q.Y!, pub, 33, 32);

        var d = new byte[32];
        CopyRightAligned(p.D!, d, 0, 32);

        return new VapidKeys(Base64Url.Encode(pub), Base64Url.Encode(d));
    }

    // Right-align `source` into destination[offset .. offset+width) (left-padding with zeros).
    internal static void CopyRightAligned(ReadOnlySpan<byte> source, Span<byte> destination, int offset, int width)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(source.Length, width);
        source.CopyTo(destination.Slice(offset + width - source.Length, source.Length));
    }
}
