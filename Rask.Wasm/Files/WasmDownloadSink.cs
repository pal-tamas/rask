using Rask.Core.Routing;

namespace Rask.Wasm.Files;

internal sealed class WasmDownloadSink : IDownloadSink
{
    private PendingDownload? _pending;

    public void Stage(string filename, byte[] bytes, string? contentType)
        => _pending = new PendingDownload(filename, contentType ?? "application/octet-stream", null, bytes);

    public void Stage(string filename, Stream stream, string? contentType)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _pending = new PendingDownload(filename, contentType ?? "application/octet-stream", null, ms.ToArray());
    }

    public bool TryConsume(out PendingDownload? download)
    {
        download = _pending;
        _pending = null;
        return download is not null;
    }
}
