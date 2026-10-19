using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Rask.Storage.Backends;

/// <summary>
/// One file's bytes as the data reader streams them, cut to the range asked for, and owning everything that has to stay
/// open while they are read: the reader, its command and the context whose connection they run on.
/// </summary>
internal sealed class DatabaseReadStream(Stream column, DbDataReader reader, DbCommand command, DbContext db, long? count)
    : Stream
{
    private long? _remaining = count;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Reads past the first <paramref name="offset"/> bytes, which a sequential reader cannot seek over.</summary>
    internal async Task SkipAsync(long offset, CancellationToken cancellationToken)
    {
        if (offset <= 0)
        {
            return;
        }

        if (column.CanSeek)
        {
            column.Seek(offset, SeekOrigin.Begin);
            return;
        }

        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (offset > 0)
            {
                var read = await column.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, offset)), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                offset -= read;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_remaining is { } remaining)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            buffer = buffer[..(int)Math.Min(buffer.Length, remaining)];
        }

        var read = await column.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _remaining -= read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            column.Dispose();
            reader.Dispose();
            command.Dispose();
            db.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await column.DisposeAsync().ConfigureAwait(false);
        await reader.DisposeAsync().ConfigureAwait(false);
        await command.DisposeAsync().ConfigureAwait(false);
        await db.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
