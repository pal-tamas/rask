using System.Buffers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Storage.Backends;
using Rask.Storage.Serving;
using Rask.Storage.Upload;

namespace Rask.Storage;

/// <summary><see cref="IFiles"/> over the application's <typeparamref name="TContext"/> and the configured store.</summary>
internal sealed class Files<TContext>(IDbContextFactory<TContext> contextFactory, StorageRuntime runtime) : IFiles
    where TContext : DbContext
{
    /// <summary>The same ceiling on every provider, so a link's lifetime never depends on where the bytes are.</summary>
    internal static readonly TimeSpan MaxTemporaryUrlLifetime = TimeSpan.FromDays(7);

    private const int CopyBufferSize = 81920;

    public Task<StoredFile> SaveAsync(RaskFile file, Action<SaveOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        // Refused before a byte is read. The copy below enforces the limit too, because a declared size is only a claim.
        var limit = runtime.Options.MaxFileSize;
        if (file.Size > limit)
        {
            throw FileRejectedException.TooLarge(file.Size, limit);
        }

        return SaveFileAsync(file, configure, limit, cancellationToken);
    }

    public async Task<StoredFile> SaveAsync(Stream content, string name, Action<SaveOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(name);

        return await SaveCoreAsync(content, name, Options(configure), cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoredFile?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Set<StoredFile>()
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<Stream?> OpenReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        EnsureActiveProvider(file);
        var stream = await runtime.Backend.OpenReadAsync(file.Key, 0, null, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            runtime.Logger.LogError("Stored file {FileId} has a row but no bytes in {Provider}.", file.Id, file.Provider);
        }

        return stream;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return false;
        }

        // The row first. Bytes without a row are an orphan the sweep removes; a row without bytes is a broken link.
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var removed = await db.Set<StoredFile>()
                .Where(f => f.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            if (removed == 0)
            {
                return false;
            }
        }

        if (file.Provider != runtime.Backend.Provider)
        {
            runtime.Logger.LogWarning(
                "Deleted stored file {FileId}; its bytes are in {SavedProvider}, which is no longer configured, and were left there.",
                file.Id, file.Provider);
            return true;
        }

        try
        {
            await runtime.Backend.DeleteAsync(file.Key, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The row is gone, so the file is deleted as far as the app can see; the sweep removes the bytes.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            runtime.Logger.LogWarning(ex,
                "Deleted stored file {FileId} but could not remove its bytes; the orphan sweep will.", file.Id);
        }

        return true;
    }

    public string Url(Guid id) =>
        runtime.Options.PublicBaseUrl is { } baseUrl
            ? baseUrl + KeyLayout.KeyOf(runtime.Options.Prefix, id, isPublic: true)
            : string.Concat(LiveOptions.PathBase, RaskStorageEndpointExtensions.RoutePrefix, "public/", id.ToString("N"));

    public async Task<string?> TemporaryUrlAsync(Guid id, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaxTemporaryUrlLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime,
                "A temporary URL lasts more than zero and at most 7 days, like files.TemporaryUrlAsync(id, TimeSpan.FromMinutes(5)).");
        }

        var file = await FindAsync(id, cancellationToken).ConfigureAwait(false);
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

        return string.Concat(LiveOptions.PathBase, RaskStorageEndpointExtensions.RoutePrefix,
            runtime.Protector.Protect(file.Id, lifetime));
    }

    public IResult Download(Guid id) => new StoredFileResult(id, StoredFileAccess.Download);

    private static SaveOptions Options(Action<SaveOptions>? configure)
    {
        var options = new SaveOptions();
        configure?.Invoke(options);
        return options;
    }

    private async Task<StoredFile> SaveFileAsync(RaskFile file, Action<SaveOptions>? configure, long limit,
        CancellationToken cancellationToken)
    {
        // The limit passed down is the storage limit: RaskFile's own default is 512 KB.
        var content = file.OpenReadStream(limit, cancellationToken);
        await using (content.ConfigureAwait(false))
        {
            return await SaveCoreAsync(content, file.Name, Options(configure), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<StoredFile> SaveCoreAsync(Stream content, string name, SaveOptions save,
        CancellationToken cancellationToken)
    {
        var options = runtime.Options;
        var backend = runtime.Backend;
        var id = Guid.NewGuid();
        var key = KeyLayout.KeyOf(options.Prefix, id, save.Public);
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
            var contentType = ContentTypePolicy.Refine(ContentSniffer.Sniff(buffer.AsSpan(0, read)), fileName);
            if (!ContentTypePolicy.IsAllowed(contentType, options.AllowedTypes))
            {
                throw FileRejectedException.TypeNotAllowed(contentType);
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            var spoolStream = OpenSpool(spool);
            await using (spoolStream.ConfigureAwait(false))
            {
                while (read > 0)
                {
                    size += read;
                    if (size > options.MaxFileSize)
                    {
                        throw FileRejectedException.TooLarge(size, options.MaxFileSize);
                    }

                    hash.AppendData(buffer, 0, read);
                    await spoolStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    read = await content.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken).ConfigureAwait(false);
                }

                await spoolStream.FlushAsync(cancellationToken).ConfigureAwait(false);

                // For the disk store the spool file BECOMES the stored file, so it reaches the disk before the rename:
                // a power cut must not leave a row pointing at an empty file. A remote store's spool is uploaded and
                // deleted moments later, and syncing it would only block a thread.
                if (backend.Provider == StorageProvider.Disk)
                {
                    spoolStream.Flush(flushToDisk: true);
                }
            }

            var headers = new BlobHeaders(
                ContentTypePolicy.ServedType(contentType),
                StoredFileHeaders.Disposition(contentType, fileName),
                save.Public ? StoredFileHeaders.PublicCacheControl : StoredFileHeaders.PrivateCacheControl);
            await backend.PutFileAsync(key, spool, size, headers, cancellationToken).ConfigureAwait(false);

            var stored = new StoredFile
            {
                Id = id,
                Name = fileName,
                ContentType = contentType,
                Size = size,
                Sha256 = Convert.ToHexStringLower(hash.GetHashAndReset()),
                Provider = backend.Provider,
                Key = key,
                Public = save.Public,
                CreatedAt = runtime.Time.GetUtcNow().UtcDateTime,
            };

            var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (db.ConfigureAwait(false))
            {
                db.Set<StoredFile>().Add(stored);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return stored;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            TryDelete(spool);
        }
    }

    private void EnsureActiveProvider(StoredFile file)
    {
        if (file.Provider != runtime.Backend.Provider)
        {
            throw new InvalidOperationException(
                $"Stored file {file.Id} was saved to {file.Provider}, but storage is configured for {runtime.Backend.Provider}. "
                + $"Changing Storage__Provider does not move existing files: copy them across, or set it back to {file.Provider}.");
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
}
