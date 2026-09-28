using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Rask.Storage.Upload;

namespace Rask.Storage.Serving;

/// <summary>The headers every stored-file response carries, whichever way it was reached.</summary>
internal static class StoredFileHeaders
{
    internal const string PublicCacheControl = "public, max-age=31536000, immutable";
    internal const string PrivateCacheControl = "private, no-store";

    /// <summary>
    /// Defence in depth for the day a type slips through: nothing in the response may load anything or run
    /// anything, and <c>sandbox</c> gives it an opaque origin even if it is navigated to directly.
    /// </summary>
    internal const string ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    internal static string Disposition(string contentType, string fileName)
    {
        var header = new ContentDispositionHeaderValue(ContentTypePolicy.IsInline(contentType) ? "inline" : "attachment");
        header.SetHttpFileName(fileName);
        return header.ToString();
    }

    internal static void Apply(HttpResponse response, StoredFile file, string cacheControl)
    {
        var headers = response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers[HeaderNames.ContentSecurityPolicy] = ContentSecurityPolicy;
        headers["Referrer-Policy"] = "no-referrer";
        headers[HeaderNames.ContentDisposition] = Disposition(file.ContentType, file.Name);
        headers[HeaderNames.CacheControl] = cacheControl;
    }

    /// <summary>
    /// One answer for unknown, private, expired and tampered alike, so a response never says whether a file
    /// exists or a token was once good.
    /// </summary>
    internal static Task NotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        return Task.CompletedTask;
    }
}
