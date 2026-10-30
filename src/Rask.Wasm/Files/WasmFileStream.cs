namespace Rask.Wasm.Files;

internal sealed class WasmFileStream : Stream
{
    private const int ChunkSize = 64 * 1024;
    private readonly CancellationToken _ct;
    private readonly string _ref;
    private long _position;

    public WasmFileStream(string @ref, long length, CancellationToken ct)
    {
        _ref = @ref;
        Length = length;
        _ct = ct;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException(
            "The browser hands a file's bytes over asynchronously, so read it with await stream.ReadAsync(…) or CopyToAsync(…).");

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= Length)
        {
            return 0;
        }

        var remaining = Length - _position;
        var requested = (int)Math.Min(Math.Min(buffer.Length, ChunkSize), remaining);
        var ct = cancellationToken.CanBeCanceled ? cancellationToken : _ct;

        // Before the browser is asked: WaitAsync hands back a chunk that is already there whatever the token
        // says, so without this a read would go on for work that is gone.
        ct.ThrowIfCancellationRequested();
        var chunk = await JSInterop.ReadFileChunkAsync(_ref, (int)_position, requested).WaitAsync(ct).ConfigureAwait(false);
        chunk.CopyTo(buffer);
        _position += chunk.Length;
        return chunk.Length;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var memory = new Memory<byte>(buffer, offset, count);
        return ReadAsync(memory, cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
