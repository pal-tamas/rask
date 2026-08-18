using System.Text.Json;
using Microsoft.JSInterop;
using Rask.Core.Forms;

namespace Rask.Native.Files;

/// <summary>
///     The native host's <see cref="IBrowserFileBackend" />. The picked <c>File</c> stays in the WebView,
///     registered under a short ref by the shared <c>rask-files.js</c> module; this reads its bytes back a
///     chunk at a time over <see cref="IJSRuntime" />, so <c>RaskFile.OpenReadStream</c> is a real stream and
///     a large upload never has to be buffered through a render payload.
/// </summary>
/// <remarks>
///     The same ref protocol the WASM backend uses — the difference is only the transport. WASM marshals a
///     <c>Uint8Array</c> straight across its <c>[JSImport]</c> bridge; the native bridge is JSON, so chunks
///     come back base64 and are decoded here.
/// </remarks>
internal sealed class NativeFileBackend(IJSRuntime js) : IBrowserFileBackend
{
    public RaskFile Create(JsonElement metadata)
    {
        var @ref = metadata.TryGetProperty("ref", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrEmpty(@ref))
        {
            throw new InvalidOperationException(
                "A file arrived without a client-side ref, so the host cannot read its bytes. The ref is "
                + "added by rask-files.js when the file input's change (or the form's submit) is captured — "
                + "this usually means the file metadata was constructed by hand rather than coming from a "
                + "file input, or the WebView is running a client script from a different Rask version.");
        }

        var name = metadata.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? string.Empty
            : string.Empty;
        var size = metadata.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number
            ? sz.GetInt64()
            : 0L;
        var contentType = metadata.TryGetProperty("type", out var ct) && ct.ValueKind == JsonValueKind.String
            ? ct.GetString() ?? "application/octet-stream"
            : "application/octet-stream";
        var lastModified = metadata.TryGetProperty("lastModified", out var lm) && lm.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(lm.GetInt64())
            : DateTimeOffset.UnixEpoch;

        return new NativeRaskFile(js, @ref, name, size, contentType, lastModified);
    }

    public void Release(IEnumerable<RaskFile> files)
    {
        // The WebView-side registry drops an input's refs when it next fires a change, so there is nothing to
        // release synchronously. Matches the WASM backend; the dispatcher calls this for symmetry with the
        // server backend, whose uploads do occupy a store until released.
    }
}

internal sealed class NativeRaskFile(
    IJSRuntime js,
    string @ref,
    string name,
    long size,
    string contentType,
    DateTimeOffset lastModified) : RaskFile
{
    public string Ref { get; } = @ref;
    public override string Name { get; } = name;
    public override long Size { get; } = size;
    public override string ContentType { get; } = contentType;
    public override DateTimeOffset LastModified { get; } = lastModified;

    public override Stream OpenReadStream(long maxAllowedSize = 512 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (Size > maxAllowedSize)
        {
            throw new IOException($"File '{Name}' is {Size} bytes, exceeds maxAllowedSize of {maxAllowedSize}.");
        }

        return new NativeFileStream(js, Ref, Size, cancellationToken);
    }
}

internal sealed class NativeFileStream(IJSRuntime js, string @ref, long length, CancellationToken ct) : Stream
{
    // Matches the WASM stream's chunk size. Each chunk costs one bridge round-trip and inflates ~33% as
    // base64 on the way back, so it wants to be large; it also lands in a JS string, so it wants to be
    // bounded. 64KB is where both the WASM client and the server upload path already sit.
    private const int ChunkSize = 64 * 1024;

    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length { get; } = length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("NativeFileStream is async-only — use ReadAsync.");

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (_position >= Length)
        {
            return 0;
        }

        var remaining = Length - _position;
        var requested = (int)Math.Min(Math.Min(buffer.Length, ChunkSize), remaining);
        var token = cancellationToken == default ? ct : cancellationToken;

        var base64 = await js.InvokeAsync<string>(
            "__raskFiles.readChunkBase64", token, @ref, _position, requested).ConfigureAwait(false);
        if (string.IsNullOrEmpty(base64))
        {
            // The registry drops a ref when the user re-picks on the same input. Ending the stream short
            // beats faulting it: the caller sees a truncated read, which is what actually happened.
            return 0;
        }

        var chunk = Convert.FromBase64String(base64);
        // Clamped rather than trusted: the length comes back from the WebView, and a chunk longer than the
        // caller's buffer would be a buffer overrun dressed up as a read.
        var copied = Math.Min(chunk.Length, buffer.Length);
        chunk.AsSpan(0, copied).CopyTo(buffer.Span);
        _position += copied;
        return copied;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
