namespace Rask.WebPush;

// base64url without padding (RFC 4648 §5) — the encoding used throughout Web Push for keys, the
// auth secret, and JWT segments. A thin wrapper over .NET's in-box System.Buffers.Text.Base64Url so
// the call sites read clearly and the no-padding contract lives in one place. The framework decoder
// accepts input with or without trailing padding and the '-'/'_' alphabet.
internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        System.Buffers.Text.Base64Url.EncodeToString(bytes);

    public static byte[] Decode(string value) =>
        System.Buffers.Text.Base64Url.DecodeFromChars(value);
}
