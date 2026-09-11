using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Rask.Storage.Upload;

namespace Rask.Storage.Serving;

/// <summary>How a request reached a file, which decides what it may see and how long it may be cached.</summary>
internal enum StoredFileAccess
{
    /// <summary><see cref="IFiles.Download"/>, behind the app's own authorization.</summary>
    Download,

    /// <summary>A temporary URL's token.</summary>
    Temporary,

    /// <summary>The public route, which serves only files saved as public.</summary>
    Public,
}

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

/// <summary>Streams one stored file: row lookup, provider check, safe headers, ranges and conditional requests.</summary>
internal sealed class StoredFileResult(Guid id, StoredFileAccess access) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var services = httpContext.RequestServices;
        var runtime = services.GetRequiredService<StorageRuntime>();
        var files = services.GetRequiredService<IFiles>();
        var cancellationToken = httpContext.RequestAborted;

        var file = await files.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (file is null || (access == StoredFileAccess.Public && !file.Public))
        {
            await StoredFileHeaders.NotFoundAsync(httpContext).ConfigureAwait(false);
            return;
        }

        if (file.Provider != runtime.Backend.Provider)
        {
            runtime.Logger.LogWarning(
                "Stored file {FileId} was saved to {SavedProvider}, but storage is configured for {ActiveProvider}; answering 404.",
                file.Id, file.Provider, runtime.Backend.Provider);
            await StoredFileHeaders.NotFoundAsync(httpContext).ConfigureAwait(false);
            return;
        }

        // Seekable either way: a file stream on disk, a lazily opened range reader over a bucket — so ranges and
        // conditional requests work the same, and a HEAD or a 304 never reads the object.
        var (rangeFrom, rangeTo) = RangeHint.Of(httpContext.Request, file.Size);
        var stream = await runtime.Backend
            .OpenForServingAsync(file.Key, file.Size, rangeFrom, rangeTo, cancellationToken)
            .ConfigureAwait(false);
        if (stream is null)
        {
            runtime.Logger.LogError(
                "Stored file {FileId} has a row but no bytes in {Provider}; answering 404.", file.Id, file.Provider);
            await StoredFileHeaders.NotFoundAsync(httpContext).ConfigureAwait(false);
            return;
        }

        StoredFileHeaders.Apply(httpContext.Response, file,
            access == StoredFileAccess.Public ? StoredFileHeaders.PublicCacheControl : StoredFileHeaders.PrivateCacheControl);

        // Range, If-Range, If-None-Match and HEAD are ASP.NET's own handling; the stream is disposed after.
        // No download name: that would force "attachment" and overwrite the disposition decided above.
        var result = TypedResults.Stream(
            stream,
            ContentTypePolicy.ServedType(file.ContentType),
            fileDownloadName: null,
            lastModified: new DateTimeOffset(DateTime.SpecifyKind(file.CreatedAt, DateTimeKind.Utc)),
            entityTag: new EntityTagHeaderValue("\"" + file.Sha256 + "\""),
            enableRangeProcessing: true);

        await result.ExecuteAsync(httpContext).ConfigureAwait(false);
    }
}
