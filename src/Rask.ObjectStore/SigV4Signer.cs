using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Rask.ObjectStore;

/// <summary>
///     Signs a request with AWS Signature Version 4 — the scheme S3 uses, and with it every S3-compatible
///     store (R2, GCS via its interop keys, MinIO, B2, Spaces). Implemented here rather than taken from a
///     cloud SDK because it is a few dozen lines of HMAC and the SDKs are large, reflection-heavy, and not
///     usable from a browser.
/// </summary>
/// <remarks>
///     <para>
///         The payload is signed as <c>UNSIGNED-PAYLOAD</c>. S3 permits this over HTTPS, and it is what
///         lets a stream be sent as it is read: signing the body would mean hashing it up front, so every
///         upload would have to be buffered or read twice. TLS already protects the body in transit; the
///         signature still covers the method, path, query and headers, so a request cannot be redirected to
///         a different key or bucket.
///     </para>
///     <para>
///         <b>Signatures expire.</b> A request more than 15 minutes off the service's clock is rejected,
///         and browser clocks are wrong often enough that this must be handled rather than assumed away —
///         see <see cref="ObjectStoreClock" />.
///     </para>
/// </remarks>
internal static class SigV4Signer
{
    internal const string UnsignedPayload = "UNSIGNED-PAYLOAD";
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private const string Terminator = "aws4_request";

    /// <summary>
    ///     Adds the <c>x-amz-*</c> headers and the <c>Authorization</c> header that authenticate
    ///     <paramref name="request" />. <paramref name="now" /> is passed in rather than read from the clock
    ///     so a skew correction can be applied and so tests can pin it.
    /// </summary>
    internal static void Sign(
        HttpRequestMessage request,
        ObjectStoreCredential credential,
        string region,
        string service,
        DateTimeOffset now)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("The request has no URI to sign.");
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var dateStamp = now.UtcDateTime.ToString("yyyyMMdd");

        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", UnsignedPayload);
        if (credential.SessionToken is { Length: > 0 } token)
        {
            request.Headers.TryAddWithoutValidation("x-amz-security-token", token);
        }

        // host and every x-amz-* header must be signed. range and if-none-match are signed too, though the
        // spec only requires them "to prevent data tampering": they are what say which bytes are being read
        // and whether an existing object may be overwritten, so leaving them unsigned would let anything in
        // the middle change the meaning of the request. They are safe to sign because this client sets them
        // itself — unlike hop-by-hop headers, which proxies rewrite and which must never be signed.
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}",
        };

        foreach (var (name, values) in request.Headers)
        {
            var lower = name.ToLowerInvariant();
            if (lower.StartsWith("x-amz-", StringComparison.Ordinal) ||
                lower is "range" or "if-none-match")
            {
                headers[lower] = string.Join(",", values).Trim();
            }
        }

        if (request.Content?.Headers.ContentType is { } contentType)
        {
            headers["content-type"] = contentType.ToString();
        }

        var signedHeaders = string.Join(";", headers.Keys);
        var canonicalHeaders = new StringBuilder();
        foreach (var (name, value) in headers)
        {
            canonicalHeaders.Append(name).Append(':').Append(value).Append('\n');
        }

        var canonicalRequest =
            $"{request.Method.Method}\n" +
            $"{CanonicalPath(uri)}\n" +
            $"{CanonicalQuery(uri)}\n" +
            $"{canonicalHeaders}\n" +
            $"{signedHeaders}\n" +
            UnsignedPayload;

        var scope = $"{dateStamp}/{region}/{service}/{Terminator}";
        var stringToSign =
            $"{Algorithm}\n{amzDate}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";

        var signingKey = SigningKey(credential.SecretAccessKey!, dateStamp, region, service);
        var signature = Hex(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            Algorithm,
            $"Credential={credential.AccessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static byte[] SigningKey(string secret, string dateStamp, string region, string service)
    {
        var kDate = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + secret), Encoding.UTF8.GetBytes(dateStamp));
        var kRegion = HMACSHA256.HashData(kDate, Encoding.UTF8.GetBytes(region));
        var kService = HMACSHA256.HashData(kRegion, Encoding.UTF8.GetBytes(service));
        return HMACSHA256.HashData(kService, Encoding.UTF8.GetBytes(Terminator));
    }

    // The path is taken unescaped and re-encoded here so it can't be double-encoded: a key containing a
    // space must sign as %20, never %2520, or the signature covers a different key than the one requested.
    private static string CanonicalPath(Uri uri)
    {
        var path = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        if (path.Length == 0)
        {
            return "/";
        }

        var segments = path.Split('/');
        var encoded = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            encoded[i] = UriEncode(segments[i]);
        }

        return "/" + string.Join("/", encoded);
    }

    private static string CanonicalQuery(Uri uri)
    {
        var query = uri.GetComponents(UriComponents.Query, UriFormat.Unescaped);
        if (query.Length == 0)
        {
            return string.Empty;
        }

        var pairs = new List<(string Key, string Value)>();
        foreach (var part in query.Split('&'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            var eq = part.IndexOf('=');
            pairs.Add(eq < 0
                ? (UriEncode(part), string.Empty)
                : (UriEncode(part[..eq]), UriEncode(part[(eq + 1)..])));
        }

        // Sorted by encoded name, then encoded value — ordinal, because the service sorts bytes.
        pairs.Sort(static (a, b) =>
        {
            var byKey = string.CompareOrdinal(a.Key, b.Key);
            return byKey != 0 ? byKey : string.CompareOrdinal(a.Value, b.Value);
        });

        return string.Join("&", pairs.Select(static p => $"{p.Key}={p.Value}"));
    }

    // RFC 3986 unreserved set, spelled out rather than delegated to Uri.EscapeDataString: the exact set of
    // characters left alone is what the signature agrees on, and it must not drift with the BCL.
    private static string UriEncode(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2"));
            }
        }

        return builder.ToString();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
