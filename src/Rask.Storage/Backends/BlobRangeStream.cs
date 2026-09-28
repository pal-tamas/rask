namespace Rask.Storage.Backends;

/// <summary>
/// A seekable, lazily opened stream over a remote object, so ASP.NET's range and conditional handling works on a
/// file that lives in a bucket.
/// </summary>
/// <remarks>
/// Nothing is fetched until the first read, so a <c>HEAD</c> or a <c>304</c> never costs a request to the store.
/// A read opens the object at the current position — for exactly the requested range when the request asked
/// for one — and a seek elsewhere closes it, so the next read reopens where it is needed.
/// </remarks>
internal sealed class BlobRangeStream(IBlobBackend backend, string key, long length, long? rangeFrom, long? rangeTo) : Stream
{
    // The caller's range start, until a read runs past the range and reopens to the end.
    private long? _rangeFrom = rangeFrom;
    private Stream? _inner;
    private bool _innerIsLimited;
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (target < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "The position cannot be before the start of the stream.");
        }
        if (target != _position)
        {
            CloseInner();
            _position = target;
        }

        return _position;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty || _position >= length)
        {
            return 0;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (_inner is null)
            {
                long? count = _rangeFrom == _position && rangeTo is { } to && to >= _position ? to - _position + 1 : null;
                _inner = await backend.OpenReadAsync(key, _position, count, cancellationToken).ConfigureAwait(false)
                         ?? throw new IOException("The stored file's bytes are missing from the store.");
                _innerIsLimited = count is not null;
            }

            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                _position += read;
                return read;
            }

            // The requested range is exhausted but the caller reads on: reopen once, to the end.
            if (!_innerIsLimited)
            {
                return 0;
            }

            CloseInner();
            _rangeFrom = null;
        }

        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseInner();
        }

        base.Dispose(disposing);
    }

    private void CloseInner()
    {
        _inner?.Dispose();
        _inner = null;
    }
}
