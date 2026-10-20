using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Rask.Storage.Backends;

/// <summary>
/// Files in the application's own database: one <see cref="StoredFileBytes"/> row per file, under the file's id.
/// </summary>
/// <remarks>
/// <para>
/// A save writes the bytes and the <see cref="StoredFile"/> row in ONE transaction (<see cref="SaveAsync"/>), so there
/// is never a row without bytes, nor bytes a save is still about to claim — which is why the listing below can hand the
/// sweep every row as already old enough.
/// </para>
/// <para>
/// The bytes are streamed both ways through the context's connection — from the spool file in, from the data reader
/// out — so a file is never held in memory whole where the provider streams (SQL Server, PostgreSQL). EF Core itself
/// only ever sees the table's ids.
/// </para>
/// </remarks>
internal sealed class DatabaseBlobBackend<TContext>(IDbContextFactory<TContext> contextFactory) : IBlobBackend
    where TContext : DbContext
{
    private readonly TempSpool _spool = new();

    public StorageProvider Provider => StorageProvider.Database;

    public long MaxSinglePutBytes => StorageOptions.MaxDatabaseBytes;

    public string CreateSpoolPath() => _spool.CreatePath();

    /// <summary>Writes the spooled bytes and <paramref name="row"/> together, or neither.</summary>
    internal async Task SaveAsync(StoredFile row, string sourcePath, long length, CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var table = BytesTable.Of(db);

            // Inside the provider's execution strategy, which a retrying one requires of a transaction the app opens.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using (transaction.ConfigureAwait(false))
                {
                    await InsertAsync(db, table, row.Id, sourcePath, length, cancellationToken).ConfigureAwait(false);
                    db.Set<StoredFile>().Add(row);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
    }

    public async Task PutFileAsync(string key, string sourcePath, long length, BlobHeaders headers,
        CancellationToken cancellationToken)
    {
        var id = IdOf(key);
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await InsertAsync(db, BytesTable.Of(db), id, sourcePath, length, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Stream?> OpenReadAsync(string key, long offset, long? count, CancellationToken cancellationToken)
    {
        var id = IdOf(key);
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var owned = false;
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var command = BytesTable.Of(db).Select(db, id);
            var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow, cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                await reader.DisposeAsync().ConfigureAwait(false);
                await command.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            // From here the stream owns the reader, the command and the context, and closes them with itself.
            var stream = new DatabaseReadStream(reader.GetStream(0), reader, command, db, count);
            owned = true;
            try
            {
                await stream.SkipAsync(offset, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return stream;
        }
        finally
        {
            if (!owned)
            {
                await db.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    // Seekable and lazy like a bucket's: a HEAD or a 304 never reads the row, and a range reads only what it asked for.
    public Task<Stream?> OpenForServingAsync(string key, long size, long? rangeFrom, long? rangeTo,
        CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(new BlobRangeStream(this, key, size, rangeFrom, rangeTo));

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var id = IdOf(key);
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Set<StoredFileBytes>().Where(b => b.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Every bytes row, by id only. Each is reported as old enough to judge: a save writes the row and its bytes
    /// together, so bytes without a row are never a save still in flight.
    /// </summary>
    public async IAsyncEnumerable<BlobEntry> ListAsync(string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var ids = db.Set<StoredFileBytes>().AsNoTracking().Select(b => b.Id).AsAsyncEnumerable();
            await foreach (var id in ids.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return new BlobEntry(KeyLayout.KeyOf(prefix, id, isPublic: false), 0, DateTimeOffset.MinValue);
            }
        }
    }

    public bool TryPresign(string key, TimeSpan lifetime, string contentType, string contentDisposition,
        [NotNullWhen(true)] out string? url)
    {
        url = null;
        return false;
    }

    public Task DeleteStaleSpoolAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        _spool.DeleteOlderThan(olderThan, cancellationToken);
        return Task.CompletedTask;
    }

    private static async Task InsertAsync(DbContext db, BytesTable table, Guid id, string sourcePath, long length,
        CancellationToken cancellationToken)
    {
        var content = TempSpool.OpenRead(sourcePath);
        await using (content.ConfigureAwait(false))
        {
            var value = table.Streams ? content : (object)await ReadAllAsync(content, length, cancellationToken).ConfigureAwait(false);
            var command = table.Insert(db, id, value, length);
            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream content, long length, CancellationToken cancellationToken)
    {
        var bytes = new byte[length];
        await content.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    private static Guid IdOf(string key) =>
        KeyLayout.TryParseId(key, out var id) ? id : throw new ArgumentException($"'{key}' is not a storage key.", nameof(key));
}
