using Rask.Core.Forms;

namespace Rask.Wasm.Files;

internal sealed class WasmRaskFile : IRaskFile
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
    public string Name { get; }
    public long Size { get; }
    public string ContentType { get; }
    public DateTimeOffset LastModified { get; }

    public Stream OpenReadStream(long maxAllowedSize = 512 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (Size > maxAllowedSize)
        {
            throw new IOException(
                $"File '{Name}' is {Size} bytes, exceeds maxAllowedSize of {maxAllowedSize}.");
        }

        // No token of its own: the stream reads under the work that opened it, so a page left mid-upload stops
        // asking the browser for the next chunk.
        return new WasmFileStream(Ref, Size, Ambient.Or(cancellationToken));
    }
}
