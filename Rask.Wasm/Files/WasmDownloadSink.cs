using System.Collections.Concurrent;
using Rask.Core.Routing;

namespace Rask.Wasm.Files;

// Token-pull download sink: keeps the bytes .NET-side and only ships a short token in the
// JSON render payload. The browser fetches the bytes via the PullDownload JSExport when the
// user-visible <a download> click fires. Replaces the prior base64-inline-in-payload path,
// which forced a ~33% JSON inflation per download plus a per-byte decodeBase64 loop in JS.
internal sealed class WasmDownloadSink : IDownloadSink
{
    private readonly ConcurrentDictionary<string, byte[]> _bytesByToken = new();
    private PendingDownload? _pending;

    public void Stage(string filename, byte[] bytes, string? contentType)
    {
        var token = Guid.NewGuid().ToString("N");
        _bytesByToken[token] = bytes;
        _pending = new PendingDownload(filename, contentType ?? "application/octet-stream", null, null, token);
    }

    public void Stage(string filename, Stream stream, string? contentType)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        Stage(filename, ms.ToArray(), contentType);
    }

    public bool TryConsume(out PendingDownload? download)
    {
        download = _pending;
        _pending = null;
        return download is not null;
    }

    // Drains the token from the in-memory map and returns the bytes once. Returning an
    // empty array on miss (rather than throwing) keeps the JS triggerDownload path tolerant
    // of double-clicks and stale tokens: the second pull just yields an empty file pickup.
    internal byte[] Pull(string token)
        => _bytesByToken.TryRemove(token, out var bytes) ? bytes : Array.Empty<byte>();
}
