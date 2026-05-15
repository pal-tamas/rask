using Rask.Core.Forms;

namespace Rask.Wasm.Files;

internal sealed class WasmRaskFile : RaskFile
{
    public WasmRaskFile(string @ref, string name, long size, string contentType, DateTimeOffset lastModified)
    {
        Ref = @ref;
        Name = name;
        Size = size;
        ContentType = contentType;
        LastModified = lastModified;
    }

    public string Ref { get; }
    public override string Name { get; }
    public override long Size { get; }
    public override string ContentType { get; }
    public override DateTimeOffset LastModified { get; }

    public override Stream OpenReadStream(long maxAllowedSize = 512 * 1024, CancellationToken cancellationToken = default)
    {
        if (Size > maxAllowedSize)
        {
            throw new IOException(
                $"File '{Name}' is {Size} bytes, exceeds maxAllowedSize of {maxAllowedSize}.");
        }

        return new WasmFileStream(Ref, Size, cancellationToken);
    }
}

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
        throw new NotSupportedException("WasmFileStream is async-only — use ReadAsync.");

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= Length)
        {
            return 0;
        }

        var remaining = Length - _position;
        var requested = (int)Math.Min(Math.Min(buffer.Length, ChunkSize), remaining);
        var ct = cancellationToken == default ? _ct : cancellationToken;
        var chunk = await JSInterop.ReadFileChunkAsync(_ref, (int)_position, requested).ConfigureAwait(false);
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
