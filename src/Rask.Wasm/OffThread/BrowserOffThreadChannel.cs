#if RASK_BROWSER
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
#endif
using Rask.Cqrs;

namespace Rask.Wasm;

// The page's line to its [OffThread] worker: postMessage, through Resources/rask-offthread.ts. That module is imported
// the first time something is sent, so a page that never sends anything never fetches it.
//
// Frames cross as bytes both ways, and never through a promise: a frame goes out as a view over .NET memory, which the
// module copies into the ArrayBuffer it transfers, and comes back through the Answered export as the Uint8Array the
// worker transferred.
internal sealed partial class BrowserOffThreadChannel : IOffThreadChannel
{
    private BrowserOffThreadChannel()
    {
    }

    // A page has one worker, and the exports below are static, so they reach it here.
    internal static BrowserOffThreadChannel Instance { get; } = new();

#if RASK_BROWSER
    private const string Module = "rask.offthread";

    private readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]>> _pending = new();
    private Task? _imported;

    public async Task<byte[]> Call(int id, byte[] frame)
    {
        await Imported().ConfigureAwait(false);
        var answer = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = answer;
        CallImport(id, frame, CultureInfo.CurrentUICulture.Name);
        return await answer.Task.ConfigureAwait(false);
    }

    public void Cancel(int id)
    {
        if (_imported is { IsCompletedSuccessfully: true })
        {
            CancelImport(id);
        }
    }

    public void Restart()
    {
        if (_imported is { IsCompletedSuccessfully: true })
        {
            RestartImport();
        }

        FailAll("The off-thread worker was restarted to stop a cancelled handler.");
    }

    public void Warm() => _ = WarmAsync();

    // The worker's answer to one request, from rask-offthread.ts.
    [JSExport]
    public static void Answered(int id, byte[] frame)
    {
        if (Instance._pending.TryRemove(id, out var answer))
        {
            answer.TrySetResult(frame);
        }
    }

    // The worker could not start, or stopped: everything waiting on it fails, and the next call starts a new one.
    [JSExport]
    public static void Stopped(string reason) => Instance.FailAll(reason);

    private void FailAll(string reason)
    {
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var answer))
            {
                answer.TrySetException(new InvalidOperationException(reason));
            }
        }
    }

    private async Task WarmAsync()
    {
        await Imported().ConfigureAwait(false);
        WarmImport(CultureInfo.CurrentUICulture.Name);
    }

    // Relative to _framework/, as the runtime's own import of rask.wasm.js is. A failed import is tried again next time.
    private Task Imported()
    {
        if (_imported is not { IsFaulted: false, IsCanceled: false })
        {
            _imported = JSHost.ImportAsync(Module, "../rask-offthread.js");
        }

        return _imported;
    }

    [JSImport("call", Module)]
    private static partial void CallImport(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> frame, string culture);

    [JSImport("cancel", Module)]
    private static partial void CancelImport(int id);

    [JSImport("restart", Module)]
    private static partial void RestartImport();

    [JSImport("warm", Module)]
    private static partial void WarmImport(string culture);
#else
    // Off the browser there is no worker to reach; Wire never installs this there.
    public Task<byte[]> Call(int id, byte[] frame) => throw new PlatformNotSupportedException();

    public void Cancel(int id)
    {
    }

    public void Restart()
    {
    }

    public void Warm()
    {
    }
#endif
}
