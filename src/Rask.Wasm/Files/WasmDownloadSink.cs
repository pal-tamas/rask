using System.Collections.Concurrent;
using Rask.Core.Routing;

namespace Rask.Wasm.Files;

// Token-pull download sink: keeps the bytes .NET-side and only ships a short token in the
// JSON render payload. The browser fetches the bytes via the PullDownload JSExport when the
// user-visible <a download> click fires. Replaces the prior base64-inline-in-payload path,
// which forced a ~33% JSON inflation per download plus a per-byte decodeBase64 loop in JS.
internal sealed class WasmDownloadSink : IDownloadSink
{
    // Bound on retained un-pulled stagings. Only the most-recently-staged download is ever shipped
    // (TryConsume reads the single _pending slot), and a real pull removes its entry — so in the
    // normal one-stage-one-pull flow the map stays near-empty. But an orphaned stage (a second
    // Stage before the first is consumed, a render coalesced away, or a token the browser never
    // pulls because the user navigated off) would otherwise leave its byte[] in the map for the
    // whole page lifetime. Evicting the oldest past this cap turns an unbounded leak into a bounded
    // working set. WASM is single-threaded, so the queue + dictionary need no extra locking.
    private const int MaxRetainedStagings = 16;

    private readonly ConcurrentDictionary<string, byte[]> _bytesByToken = new();
    private readonly Queue<string> _order = new();
    private PendingDownload? _pending;

    // Test seam: how many staged downloads are currently retained.
    internal int RetainedCount => _bytesByToken.Count;

    public void Stage(string filename, byte[] bytes, string? contentType)
    {
        var token = Guid.NewGuid().ToString("N");
        _bytesByToken[token] = bytes;
        _order.Enqueue(token);
        while (_order.Count > MaxRetainedStagings && _order.TryDequeue(out var oldest))
        {
            // No-op when `oldest` was already pulled — TryRemove just returns false.
            _bytesByToken.TryRemove(oldest, out _);
        }

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
