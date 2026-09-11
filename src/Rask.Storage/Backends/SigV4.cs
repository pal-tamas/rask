using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Rask.Storage.Backends;

/// <summary>An S3 access key. A class, not a record: a record's generated <c>ToString</c> would print the secret.</summary>
internal sealed class S3Credential(string accessKeyId, string secretAccessKey, string? sessionToken)
{
    public string AccessKeyId { get; } = accessKeyId;

    public string SecretAccessKey { get; } = secretAccessKey;

    public string? SessionToken { get; } = sessionToken;

    public override string ToString() => "S3 access key " + AccessKeyId;
}

/// <summary>
/// AWS Signature Version 4, for S3 and every store that speaks its API — header signing for requests, query
/// signing for presigned URLs.
/// </summary>
/// <remarks>
/// <para>
/// The canonical path and query are built here from RAW parts, and the URL that is sent is built from the very
/// same encoded strings. Reconstructing them from a parsed <see cref="Uri"/> instead is how a
/// <c>response-content-disposition</c> holding <c>&amp;</c> gets split in two after unescaping, and the service
/// rejects a signature over a different query than the one it received.
/// </para>
/// <para>
/// The payload is <c>UNSIGNED-PAYLOAD</c>: signing the body would mean hashing it up front, TLS protects it in
/// transit, and the signature still binds the method, path, query and headers.
/// </para>
/// </remarks>
internal static class SigV4
{
    internal const string UnsignedPayload = "UNSIGNED-PAYLOAD";
    internal const string Algorithm = "AWS4-HMAC-SHA256";
    internal const long MaxPresignSeconds = 7 * 24 * 60 * 60;

    private const string Service = "s3";
    private const string Terminator = "aws4_request";

    internal static string CanonicalPath(IEnumerable<string> segments) =>
        "/" + string.Join('/', segments.Select(UriEncode));

    internal static string CanonicalQuery(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Join('&', pairs
            .Select(static p => (Key: UriEncode(p.Key), Value: UriEncode(p.Value)))
            .OrderBy(static p => p.Key, StringComparer.Ordinal)
            .ThenBy(static p => p.Value, StringComparer.Ordinal)
            .Select(static p => p.Key + "=" + p.Value));

    /// <summary>
    /// Signs <paramref name="request"/>, whose URI must be <c>scheme://host{canonicalPath}?{canonicalQuery}</c>.
    /// Signs <c>host</c>, every <c>x-amz-*</c> header, <c>range</c>, and the content headers the object is stored
    /// with — the ones this client sets itself, never hop-by-hop headers a proxy may rewrite.
    /// </summary>
    internal static void SignHeaders(HttpRequestMessage request, string canonicalPath, string canonicalQuery,
        S3Credential credential, string region, DateTimeOffset now, string payloadHash = UnsignedPayload)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("The request has no URI to sign.");
        var (amzDate, dateStamp) = Stamps(now);

        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        if (credential.SessionToken is { Length: > 0 } token)
        {
            request.Headers.TryAddWithoutValidation("x-amz-security-token", token);
        }

        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["host"] = HostOf(uri) };
        foreach (var (name, values) in request.Headers.NonValidated)
        {
            var lower = name.ToLowerInvariant();
            if (lower.StartsWith("x-amz-", StringComparison.Ordinal) || lower is "range" or "cache-control")
            {
                headers[lower] = HeaderValue(values);
            }
        }

        if (request.Content is not null)
        {
            foreach (var (name, values) in request.Content.Headers.NonValidated)
            {
                var lower = name.ToLowerInvariant();
                if (lower is "content-type" or "content-disposition")
                {
                    headers[lower] = HeaderValue(values);
                }
            }
        }

        var signedHeaders = string.Join(';', headers.Keys);
        var scope = Scope(dateStamp, region);
        var canonical = CanonicalRequest(request.Method.Method, canonicalPath, canonicalQuery, headers, signedHeaders, payloadHash);
        var signature = Sign(credential.SecretAccessKey, dateStamp, region, StringToSign(amzDate, scope, canonical));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            Algorithm,
            $"Credential={credential.AccessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    /// <summary>
    /// A presigned GET URL: every <c>X-Amz-*</c> parameter and <paramref name="extraQuery"/> (the
    /// <c>response-*</c> overrides) are part of the signed canonical query.
    /// </summary>
    internal static string Presign(string scheme, string host, IEnumerable<string> segments,
        IEnumerable<KeyValuePair<string, string>> extraQuery, S3Credential credential, string region,
        DateTimeOffset now, TimeSpan lifetime)
    {
        var seconds = (long)Math.Ceiling(lifetime.TotalSeconds);
        if (seconds < 1 || seconds > MaxPresignSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "A presigned URL lasts 1 second to 7 days.");
        }

        var (amzDate, dateStamp) = Stamps(now);
        var scope = Scope(dateStamp, region);

        var query = new List<KeyValuePair<string, string>>(extraQuery)
        {
            new("X-Amz-Algorithm", Algorithm),
            new("X-Amz-Credential", credential.AccessKeyId + "/" + scope),
            new("X-Amz-Date", amzDate),
            new("X-Amz-Expires", seconds.ToString(CultureInfo.InvariantCulture)),
            new("X-Amz-SignedHeaders", "host"),
        };

        if (credential.SessionToken is { Length: > 0 } token)
        {
            query.Add(new("X-Amz-Security-Token", token));
        }

        var canonicalPath = CanonicalPath(segments);
        var canonicalQuery = CanonicalQuery(query);
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["host"] = host };
        var canonical = CanonicalRequest("GET", canonicalPath, canonicalQuery, headers, "host", UnsignedPayload);
        var signature = Sign(credential.SecretAccessKey, dateStamp, region, StringToSign(amzDate, scope, canonical));

        return $"{scheme}://{host}{canonicalPath}?{canonicalQuery}&X-Amz-Signature={signature}";
    }

    internal static string CanonicalRequest(string method, string canonicalPath, string canonicalQuery,
        SortedDictionary<string, string> headers, string signedHeaders, string payloadHash)
    {
        var builder = new StringBuilder()
            .Append(method).Append('\n')
            .Append(canonicalPath).Append('\n')
            .Append(canonicalQuery).Append('\n');

        foreach (var (name, value) in headers)
        {
            builder.Append(name).Append(':').Append(value).Append('\n');
        }

        return builder.Append('\n').Append(signedHeaders).Append('\n').Append(payloadHash).ToString();
    }

    internal static string HostOf(Uri uri) =>
        uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);

    // RFC 3986 unreserved set, spelled out rather than delegated to Uri.EscapeDataString: the exact set of
    // characters left alone is what the signature agrees on, and it must not drift with the BCL.
    internal static string UriEncode(string value)
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
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static string StringToSign(string amzDate, string scope, string canonicalRequest) =>
        $"{Algorithm}\n{amzDate}\n{scope}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";

    private static string Sign(string secret, string dateStamp, string region, string stringToSign)
    {
        var kDate = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + secret), Encoding.UTF8.GetBytes(dateStamp));
        var kRegion = HMACSHA256.HashData(kDate, Encoding.UTF8.GetBytes(region));
        var kService = HMACSHA256.HashData(kRegion, Encoding.UTF8.GetBytes(Service));
        var kSigning = HMACSHA256.HashData(kService, Encoding.UTF8.GetBytes(Terminator));
        return Convert.ToHexStringLower(HMACSHA256.HashData(kSigning, Encoding.UTF8.GetBytes(stringToSign)));
    }

    private static string Scope(string dateStamp, string region) => $"{dateStamp}/{region}/{Service}/{Terminator}";

    private static (string AmzDate, string DateStamp) Stamps(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return (utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
            utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
    }

    // Trimmed, with runs of spaces collapsed to one, as the specification's canonical header value requires.
    private static string HeaderValue(HeaderStringValues values)
    {
        var joined = string.Join(',', values).Trim();
        var builder = new StringBuilder(joined.Length);
        var previousSpace = false;
        foreach (var c in joined)
        {
            var space = c == ' ';
            if (!(space && previousSpace))
            {
                builder.Append(c);
            }

            previousSpace = space;
        }

        return builder.ToString();
    }
}
