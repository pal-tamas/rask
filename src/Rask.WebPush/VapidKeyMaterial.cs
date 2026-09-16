using System.Security.Cryptography;

namespace Rask.WebPush;

// The VAPID key generation itself, held apart from the public VapidKeys record that wraps it.
//
// Two callers, one implementation. Rask.WebPush hands the pair out as VapidKeys; `rask new --push`
// mints a development pair while it is still writing files to disk, and Rask.Cli source-links THIS
// file rather than taking a package reference on a library it needs twenty lines from. That is also
// why the class is internal and returns a tuple: source-linking the public record instead would put
// a type on the tool's public API surface, which is otherwise empty (PublicAPI/net10.0), and a tool
// has no public surface to spend.
internal static class VapidKeyMaterial
{
    /// <summary>
    ///     Creates a fresh P-256 pair in the base64url form both the browser and this sender expect.
    /// </summary>
    public static (string PublicKey, string PrivateKey) Generate()
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

        return (Base64Url.Encode(pub), Base64Url.Encode(d));
    }

    // Right-align `source` into destination[offset .. offset+width) (left-padding with zeros).
    internal static void CopyRightAligned(ReadOnlySpan<byte> source, Span<byte> destination, int offset, int width)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(source.Length, width);
        source.CopyTo(destination.Slice(offset + width - source.Length, source.Length));
    }
}
