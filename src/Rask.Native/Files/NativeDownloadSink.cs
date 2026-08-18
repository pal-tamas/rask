using System.Collections.Concurrent;
using Rask.Core.Routing;

namespace Rask.Native.Files;

/// <summary>
///     Token-pull <see cref="IDownloadSink" /> for the native host — the same wire contract the WASM sink
///     uses: the bytes stay .NET-side and only a short token rides in the render payload, which the client
///     hands straight back on a <c>{"type":"download"}</c> message. Nothing is base64'd through the WebView
///     bridge.
/// </summary>
/// <remarks>
///     Where the browser hosts finish by letting the page download the bytes, the native host has no such
///     affordance: <c>&lt;a download&gt;</c> does nothing useful in a WKWebView, and a file written into the
///     app's own sandbox is invisible to the user on iOS unless the app opts into file sharing. So
///     <c>NativeAppHost</c> pulls the bytes here and hands them to <see cref="INativeFileExport" />, whose
///     platform implementations present the OS share sheet — the mobile meaning of "here is a file for you".
///     <para>
///         Unlike the WASM sink this is thread-safe: the native session fans render continuations onto the
///         thread pool, so a stage and a pull can genuinely race.
///     </para>
/// </remarks>
internal sealed class NativeDownloadSink : IDownloadSink
{
    // Same bound and reasoning as WasmDownloadSink: only the most recently staged download is ever shipped,
    // and a real pull removes its entry, so the map stays near-empty in the normal stage→pull flow. Orphans
    // (a second Stage before the first is consumed, a token the user never triggers because they navigated
    // away) would otherwise retain their byte[] for the whole app lifetime. Evicting past the cap turns an
    // unbounded leak into a bounded working set.
    private const int MaxRetainedStagings = 16;

    private readonly ConcurrentDictionary<string, StagedDownload> _byToken = new();
    private readonly Lock _gate = new();
    private readonly Queue<string> _order = new();
    private PendingDownload? _pending;

    /// <summary>Test seam: how many staged downloads are currently retained.</summary>
    internal int RetainedCount => _byToken.Count;

    public void Stage(string filename, byte[] bytes, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var token = Guid.NewGuid().ToString("N");
        var resolvedContentType = contentType ?? "application/octet-stream";
        // The name and type are retained alongside the bytes, not just put in the payload: the client echoes
        // back only the token, and by then the PendingDownload that carried the name has been consumed into
        // the frame. Without this the file would reach the share sheet called "download".
        _byToken[token] = new StagedDownload(filename, resolvedContentType, bytes);

        lock (_gate)
        {
            _order.Enqueue(token);
            while (_order.Count > MaxRetainedStagings && _order.TryDequeue(out var oldest))
            {
                // No-op when `oldest` was already pulled — TryRemove just returns false.
                _byToken.TryRemove(oldest, out _);
            }

            _pending = new PendingDownload(filename, resolvedContentType, null, null, token);
        }
    }

    public void Stage(string filename, Stream stream, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        Stage(filename, ms.ToArray(), contentType);
    }

    public bool TryConsume(out PendingDownload? download)
    {
        lock (_gate)
        {
            download = _pending;
            _pending = null;
            return download is not null;
        }
    }

    /// <summary>
    ///     Drains the token and returns the staged file once. Returns <c>null</c> on a miss rather than
    ///     throwing, so a stale or replayed token from the WebView is a no-op instead of a crash — the client
    ///     is the one place a token can arrive twice.
    /// </summary>
    internal StagedDownload? Pull(string token) => _byToken.TryRemove(token, out var staged) ? staged : null;

    /// <summary>Bytes held for one token, with the name and type they should reach the user under.</summary>
    internal sealed record StagedDownload(string FileName, string ContentType, byte[] Bytes);
}
