namespace Rask.Wasm.Files;

// What the reader's cancel of an upload reaches in the browser host: the handler that receives the files.
//
// The files are known by the refs the tab registered them under (rask-files.ts). A handler that receives them opens
// a cancellation over those refs and closes it when it is done (WasmFileBackend); the cancel button calls Cancel
// with the same refs (JSInterop.CancelUpload). A cancel can come first — the event is still queued behind
// another handler — and is kept until the handler opens its cancellation, which then starts out cancelled.
//
// One tab, one thread: the browser runs .NET on a single thread, so the lock is for the tests that run off it.
internal static class WasmUploadCancels
{
    // A cancel pressed as the handler finished finds nothing open and is kept for nobody: a few refs at most.
    private const int MaxKept = 64;

    private static readonly Lock _gate = new();
    private static readonly Dictionary<string, CancellationTokenSource> _open = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _cancelledEarly = new(StringComparer.Ordinal);

    /// <summary>The cancellation of the handler that receives the files under <paramref name="refs" />.</summary>
    internal static CancellationToken Open(IEnumerable<string> refs)
    {
        var source = new CancellationTokenSource();
        lock (_gate)
        {
            foreach (var @ref in refs)
            {
                _open[@ref] = source;
                if (_cancelledEarly.Remove(@ref))
                {
                    source.Cancel();
                }
            }
        }

        return source.Token;
    }

    /// <summary>The handler is done with the files under <paramref name="refs" />.</summary>
    internal static void Close(IEnumerable<string> refs)
    {
        var closed = new HashSet<CancellationTokenSource>();
        lock (_gate)
        {
            foreach (var @ref in refs)
            {
                if (_open.Remove(@ref, out var source))
                {
                    closed.Add(source);
                }
            }
        }

        foreach (var source in closed)
        {
            source.Dispose();
        }
    }

    /// <summary>The reader called off the upload of the files under <paramref name="refs" />.</summary>
    internal static void Cancel(IEnumerable<string> refs)
    {
        var cancelled = new HashSet<CancellationTokenSource>();
        lock (_gate)
        {
            foreach (var @ref in refs)
            {
                if (_open.TryGetValue(@ref, out var source))
                {
                    cancelled.Add(source);
                }
                else if (_cancelledEarly.Count < MaxKept)
                {
                    _cancelledEarly.Add(@ref);
                }
            }
        }

        foreach (var source in cancelled)
        {
            source.Cancel();
        }
    }
}
