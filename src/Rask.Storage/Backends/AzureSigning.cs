using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Rask.Storage.Backends;

/// <summary>Azure Storage Shared Key authorization, for requests signed with the account key.</summary>
internal static class AzureSharedKey
{
    /// <summary>The service version every request and every SAS is made against; pinned, so a signature never drifts.</summary>
    internal const string Version = "2024-11-04";

    internal static void Sign(HttpRequestMessage request, AzureAccount account, DateTimeOffset now)
    {
        request.Headers.TryAddWithoutValidation("x-ms-date", now.UtcDateTime.ToString("R", CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("x-ms-version", Version);

        var stringToSign = StringToSign(request, account.AccountName);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(account.AccountKey!, Encoding.UTF8.GetBytes(stringToSign)));
        request.Headers.Authorization = new AuthenticationHeaderValue("SharedKey", account.AccountName + ":" + signature);
    }

    /// <summary>
    /// The Blob service string-to-sign: the verb, eleven standard headers (<c>Content-Length</c> empty when
    /// zero, <c>Date</c> empty because <c>x-ms-date</c> is sent), the canonicalized <c>x-ms-*</c> headers, then
    /// the canonicalized resource with each query parameter on its own line.
    /// </summary>
    internal static string StringToSign(HttpRequestMessage request, string accountName)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("The request has no URI to sign.");
        var length = request.Content?.Headers.ContentLength is > 0 and var contentLength
            ? contentLength.ToString(CultureInfo.InvariantCulture)
            : "";

        var builder = new StringBuilder()
            .Append(request.Method.Method).Append('\n')
            .Append(ContentHeader(request, "Content-Encoding")).Append('\n')
            .Append(ContentHeader(request, "Content-Language")).Append('\n')
            .Append(length).Append('\n')
            .Append(ContentHeader(request, "Content-MD5")).Append('\n')
            .Append(ContentHeader(request, "Content-Type")).Append('\n')
            .Append('\n')
            .Append(RequestHeader(request, "If-Modified-Since")).Append('\n')
            .Append(RequestHeader(request, "If-Match")).Append('\n')
            .Append(RequestHeader(request, "If-None-Match")).Append('\n')
            .Append(RequestHeader(request, "If-Unmodified-Since")).Append('\n')
            .Append(RequestHeader(request, "Range")).Append('\n');

        var msHeaders = request.Headers.NonValidated
            .Where(static h => h.Key.StartsWith("x-ms-", StringComparison.OrdinalIgnoreCase))
            .Select(static h => (Name: h.Key.ToLowerInvariant(), Value: Collapse(string.Join(',', h.Value))))
            .OrderBy(static h => h.Name, StringComparer.Ordinal);
        foreach (var (name, value) in msHeaders)
        {
            builder.Append(name).Append(':').Append(value).Append('\n');
        }

        builder.Append('/').Append(accountName).Append(uri.AbsolutePath);

        var parameters = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part =>
            {
                var eq = part.IndexOf('=', StringComparison.Ordinal);
                return eq < 0
                    ? (Name: Uri.UnescapeDataString(part).ToLowerInvariant(), Value: "")
                    : (Name: Uri.UnescapeDataString(part[..eq]).ToLowerInvariant(), Value: Uri.UnescapeDataString(part[(eq + 1)..]));
            })
            .GroupBy(static p => p.Name, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            builder.Append('\n').Append(parameter.Key).Append(':')
                .Append(string.Join(',', parameter.Select(static p => p.Value).Order(StringComparer.Ordinal)));
        }

        return builder.ToString();
    }

    private static string ContentHeader(HttpRequestMessage request, string name) =>
        request.Content is not null && request.Content.Headers.NonValidated.TryGetValues(name, out var values)
            ? string.Join(',', values)
            : "";

    private static string RequestHeader(HttpRequestMessage request, string name) =>
        request.Headers.NonValidated.TryGetValues(name, out var values) ? string.Join(',', values) : "";

    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousSpace = false;
        foreach (var c in value.Trim())
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

/// <summary>
/// A read-only service SAS for one blob, signed with the account key — what makes an Azure temporary URL a
/// download that never passes through the app. <c>st</c> is omitted, so a clock slightly behind Azure's cannot
/// produce a URL that is not valid yet.
/// </summary>
internal static class AzureSas
{
    internal static string BlobRead(AzureAccount account, string container, string blobName, DateTimeOffset expiry,
        string contentType, string contentDisposition)
    {
        var expires = expiry.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var protocol = account.BlobEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "https" : "https,http";
        var stringToSign = StringToSign(account.AccountName, container, blobName, expires, protocol, contentType, contentDisposition);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(account.AccountKey!, Encoding.UTF8.GetBytes(stringToSign)));

        return string.Join('&',
            Pair("sv", AzureSharedKey.Version),
            Pair("spr", protocol),
            Pair("se", expires),
            Pair("sr", "b"),
            Pair("sp", "r"),
            Pair("rscd", contentDisposition),
            Pair("rsct", contentType),
            Pair("sig", signature));
    }

    /// <summary>The sixteen-field service SAS string-to-sign for versions 2020-12-06 and later.</summary>
    internal static string StringToSign(string accountName, string container, string blobName, string expires,
        string protocol, string contentType, string contentDisposition) =>
        string.Join('\n',
            "r",                                          // signedPermissions
            "",                                           // signedStart
            expires,                                      // signedExpiry
            $"/blob/{accountName}/{container}/{blobName}", // canonicalizedResource
            "",                                           // signedIdentifier
            "",                                           // signedIP
            protocol,                                     // signedProtocol
            AzureSharedKey.Version,                       // signedVersion
            "b",                                          // signedResource
            "",                                           // signedSnapshotTime
            "",                                           // signedEncryptionScope
            "",                                           // rscc
            contentDisposition,                           // rscd
            "",                                           // rsce
            "",                                           // rscl
            contentType);                                 // rsct

    private static string Pair(string name, string value) => name + "=" + Uri.EscapeDataString(value);
}
