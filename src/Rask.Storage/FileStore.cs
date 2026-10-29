using System.Buffers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rask.Data;
using Rask.Storage.Backends;
using Rask.Storage.Serving;
using Rask.Storage.Upload;

namespace Rask.Storage;

/// <summary><see cref="IFiles"/> over the application's <typeparamref name="TContext"/> and the configured store.</summary>
internal sealed partial class FileStore<TContext>(IDbContextFactory<TContext> contextFactory, StorageRuntime runtime)
    : IFiles, IStatedTypeFiles
    where TContext : DbContext
{
    /// <summary>The same ceiling on every provider, so a link's lifetime never depends on where the bytes are.</summary>
    internal static TimeSpan MaxTemporaryUrlLifetime => TimeSpan.FromDays(7);

    private const int CopyBufferSize = 81920;

    public Task<StoredFile> Add(
        Func<long, CancellationToken, Stream> openRead, string name, long size, bool isPublic, CancellationToken cancellationToken = default) =>
        AddOpened(openRead, name, size, isPublic, null, cancellationToken);

    public Task<StoredFile> Add(Stream content, string name, bool isPublic, CancellationToken cancellationToken = default) =>
        AddContent(content, name, isPublic, null, cancellationToken);

    public Task<StoredFile> Add(
        Func<long, CancellationToken, Stream> openRead, string name, long size, bool isPublic, string contentType,
        CancellationToken cancellationToken) =>
        AddOpened(openRead, name, size, isPublic, contentType, cancellationToken);

    public Task<StoredFile> Add(Stream content, string name, bool isPublic, string contentType, CancellationToken cancellationToken) =>
        AddContent(content, name, isPublic, contentType, cancellationToken);

    private Task<StoredFile> AddOpened(
        Func<long, CancellationToken, Stream> openRead, string name, long size, bool isPublic, string? statedType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openRead);
        ArgumentNullException.ThrowIfNull(name);

        // Refused before a byte is read. The copy below enforces the limit too, because a declared size is only a claim.
        var limit = runtime.Options.MaxFileSize;
        if (size > limit)
        {
            throw FileRejectedException.TooLarge(size, limit);
        }

        return SaveOpenedAsync(openRead, name, isPublic, statedType, limit, cancellationToken);
    }

    private Task<StoredFile> AddContent(Stream content, string name, bool isPublic, string? statedType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(name);

        return SaveCoreAsync(content, name, isPublic, statedType, cancellationToken);
    }

    public async Task<StoredFile?> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Scoped to the tenant in flight, so one tenant cannot reach another's file even holding its
            // id. Null matches the files that belong to nobody, which is what the host's own saves produce.
            var tenant = Tenant.Key;

            return await db.Set<StoredFile>()
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenant, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<Stream?> OpenRead(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await Get(id, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        EnsureActiveProvider(file);
        var stream = await runtime.Backend.OpenReadAsync(file.Key, 0, null, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            BytesMissing(runtime.Logger, file.Id, file.Provider);
        }

        return stream;
    }

    public async Task<bool> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        StoredFile? file;
        var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Scoped like the read: deleting another tenant's file has to be as impossible as reading it.
            var tenant = Tenant.Key;
            var rows = db.Set<StoredFile>().Where(f => f.Id == id && f.TenantId == tenant);

            // The row first. Bytes without a row are an orphan the sweep removes; a row without bytes is a broken link.
            file = await rows.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (file is null || await rows.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                return false;
            }
        }

        // Inside Db.Transaction the row goes with the work, and the bytes only once it has committed.
        if (AmbientTransaction.Current is { } transaction)
        {
            transaction.AfterCommit(() => RemoveBytesAsync(file, CancellationToken.None));
            return true;
        }

        await RemoveBytesAsync(file, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public string Url(Guid id) =>
        runtime.Options.PublicBaseUrl is { } baseUrl
            ? baseUrl + KeyLayout.KeyOf(runtime.Options.Prefix, id, isPublic: true)
            : string.Concat(RaskPathBase.Current, RaskStorageEndpointExtensions.RoutePrefix, "public/", id.ToString("N"));

    public async Task<string?> SharedUrl(Guid id, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaxTemporaryUrlLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime,
                "A temporary URL lasts more than zero and at most 7 days, like files.Share(id).For(TimeSpan.FromMinutes(5)).");
        }

        var file = await Get(id, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        EnsureActiveProvider(file);

        var contentType = ContentTypePolicy.ServedType(file.ContentType);
        var disposition = StoredFileHeaders.Disposition(file.ContentType, file.Name);
        if (runtime.Backend.TryPresign(file.Key, lifetime, contentType, disposition, out var signed))
        {
            return signed;
        }

        return string.Concat(RaskPathBase.Current, RaskStorageEndpointExtensions.RoutePrefix,
            runtime.Protector.Protect(file.Id, lifetime));
    }

    public IResult Download(Guid id) => new StoredFileResult(id, StoredFileAccess.Download);

    private async Task<StoredFile> SaveOpenedAsync(Func<long, CancellationToken, Stream> openRead, string name,
        bool isPublic, string? statedType, long limit, CancellationToken cancellationToken)
    {
        // The limit handed to the opener is the storage limit: an upload's own default (512 KB for IRaskFile) would
        // otherwise refuse anything larger, however high MaxFileSize is set.
        var content = openRead(limit, cancellationToken);
        await using (content.ConfigureAwait(false))
        {
            return await SaveCoreAsync(content, name, isPublic, statedType, cancellationToken).ConfigureAwait(false);
        }
    }

    // statedType is what .As(…) asked for, already validated as a plain media type; null, the bytes decide.
    private async Task<StoredFile> SaveCoreAsync(Stream content, string name, bool isPublic, string? statedType,
        CancellationToken cancellationToken)
    {
        var options = runtime.Options;
        var backend = runtime.Backend;
        var id = Guid.NewGuid();
        var key = KeyLayout.KeyOf(options.Prefix, id, isPublic);
        var fileName = SafeFileName.Clean(name);

        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        var spool = backend.CreateSpoolPath();
        try
        {
            // Sniffed and checked against AllowedTypes before anything is written anywhere.
            var read = await content
                .ReadAtLeastAsync(buffer.AsMemory(0, ContentSniffer.HeadLength), ContentSniffer.HeadLength,
                    throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
            var contentType = statedType ?? ContentTypePolicy.Refine(ContentSniffer.Sniff(buffer.AsSpan(0, read)), fileName);
            if (!ContentTypePolicy.IsAllowed(contentType, options.AllowedTypes))
            {
                throw FileRejectedException.TypeNotAllowed(contentType);
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var size = await SpoolAsync(content, buffer, read, spool, hash, cancellationToken).ConfigureAwait(false);

            var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            StoredFile Row() => StoredFile.For(
                id, fileName, contentType, size, sha256, backend.Provider, key, isPublic, runtime.Time.GetUtcNow().UtcDateTime);

            // In the database, the bytes and the row are one transaction — Db.Transaction's, when one is in progress.
            if (backend is DatabaseBlobBackend<TContext> database)
            {
                var row = Row();
                await database.SaveAsync(row, spool, size, cancellationToken).ConfigureAwait(false);
                return row;
            }

            // Opened before the bytes are written, so a row that cannot join the transaction in progress refuses first.
            var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (db.ConfigureAwait(false))
            {
                var headers = new BlobHeaders(
                    ContentTypePolicy.ServedType(contentType),
                    StoredFileHeaders.Disposition(contentType, fileName),
                    isPublic ? StoredFileHeaders.PublicCacheControl : StoredFileHeaders.PrivateCacheControl);
                await backend.PutFileAsync(key, spool, size, headers, cancellationToken).ConfigureAwait(false);
                AmbientTransaction.Current?.OnRollback(() => RemoveRolledBackAsync(id, key));

                var stored = Row();
                db.Set<StoredFile>().Add(stored);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return stored;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            TryDelete(spool);
        }
    }

    /// <summary>
    /// Copies <paramref name="content"/> — whose first <paramref name="read"/> bytes are already in
    /// <paramref name="buffer"/> — to the spool file, hashing it and enforcing the size limit. Returns the size.
    /// </summary>
    private async Task<long> SpoolAsync(
        Stream content, byte[] buffer, int read, string spool, IncrementalHash hash, CancellationToken cancellationToken)
    {
        var maxFileSize = runtime.Options.MaxFileSize;
        long size = 0;
        var spoolStream = OpenSpool(spool);
        await using (spoolStream.ConfigureAwait(false))
        {
            while (read > 0)
            {
                size += read;
                if (size > maxFileSize)
                {
                    throw FileRejectedException.TooLarge(size, maxFileSize);
                }

                hash.AppendData(buffer, 0, read);
                await spoolStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                read = await content.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken).ConfigureAwait(false);
            }

            await spoolStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            // For the disk store the spool file BECOMES the stored file, so it reaches the disk before the rename:
            // a power cut must not leave a row pointing at an empty file. A remote store's spool is uploaded and
            // deleted moments later, and syncing it would only block a thread.
            if (runtime.Backend.Provider == StorageProvider.Disk)
            {
                spoolStream.Flush(flushToDisk: true);
            }
        }

        return size;
    }

    /// <summary>A context on the application's database, joined to the <c>Db.Transaction</c> in progress if there is one.</summary>
    private async Task<TContext> OpenAsync(CancellationToken cancellationToken) =>
        AmbientTransaction.Join(await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false));

    private async Task RemoveBytesAsync(StoredFile file, CancellationToken cancellationToken)
    {
        if (file.Provider != runtime.Backend.Provider)
        {
            BytesLeftInOldProvider(runtime.Logger, file.Id, file.Provider);
            return;
        }

        try
        {
            await runtime.Backend.DeleteAsync(file.Key, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The row is gone, so the file is deleted as far as the app can see; the sweep removes the bytes.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            BytesNotRemoved(runtime.Logger, ex, file.Id);
        }
    }

    // The work threw, so the row never commits: the bytes it would have claimed are taken back.
    private async Task RemoveRolledBackAsync(Guid id, string key)
    {
        try
        {
            await runtime.Backend.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A cleanup on the way out of a failure must not replace it; the sweep removes the bytes.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RolledBackBytesNotRemoved(runtime.Logger, ex, id);
        }
    }

    private void EnsureActiveProvider(StoredFile file)
    {
        if (file.Provider != runtime.Backend.Provider)
        {
            throw new InvalidOperationException(
                $"Stored file {file.Id} was saved to {file.Provider}, but storage is configured for {runtime.Backend.Provider}. "
                + $"Changing Rask__Storage__Provider does not move existing files: copy them across, or set it back to {file.Provider}.");
        }
    }

    private static FileStream OpenSpool(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            BufferSize = 0,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        }

        return new FileStream(path, options);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for DeleteStaleSpoolAsync.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Stored file {FileId} has a row but no bytes in {Provider}.")]
    private static partial void BytesMissing(ILogger logger, Guid fileId, StorageProvider provider);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deleted stored file {FileId}; its bytes are in {SavedProvider}, which is no longer configured, and were left there.")]
    private static partial void BytesLeftInOldProvider(ILogger logger, Guid fileId, StorageProvider savedProvider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleted stored file {FileId} but could not remove its bytes; the orphan sweep will.")]
    private static partial void BytesNotRemoved(ILogger logger, Exception exception, Guid fileId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Stored file {FileId} was saved in a transaction that rolled back, but its bytes could not be removed; the orphan sweep will.")]
    private static partial void RolledBackBytesNotRemoved(ILogger logger, Exception exception, Guid fileId);
}
