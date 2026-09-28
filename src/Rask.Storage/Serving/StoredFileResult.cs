using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Rask.Storage.Upload;

namespace Rask.Storage.Serving;

/// <summary>Streams one stored file: row lookup, provider check, safe headers, ranges and conditional requests.</summary>
internal sealed partial class StoredFileResult(Guid id, StoredFileAccess access) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var services = httpContext.RequestServices;
        var runtime = services.GetRequiredService<StorageRuntime>();
        var files = services.GetRequiredService<IFiles>();
        var cancellationToken = httpContext.RequestAborted;

        var file = await files.Get(id, cancellationToken).ConfigureAwait(false);
        if (file is null || (access == StoredFileAccess.Public && !file.Public))
        {
            await StoredFileHeaders.NotFoundAsync(httpContext).ConfigureAwait(false);
            return;
        }

        if (file.Provider != runtime.Backend.Provider)
        {
            SavedElsewhere(runtime.Logger, file.Id, file.Provider, runtime.Backend.Provider);
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
            BytesMissing(runtime.Logger, file.Id, file.Provider);
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

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Stored file {FileId} was saved to {SavedProvider}, but storage is configured for {ActiveProvider}; answering 404.")]
    private static partial void SavedElsewhere(ILogger logger, Guid fileId, StorageProvider savedProvider, StorageProvider activeProvider);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stored file {FileId} has a row but no bytes in {Provider}; answering 404.")]
    private static partial void BytesMissing(ILogger logger, Guid fileId, StorageProvider provider);
}
