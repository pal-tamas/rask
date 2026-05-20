using System.Collections.Concurrent;
using System.Text.Json;

namespace Rask.Core.Live;

/// <summary>
///     Process-wide correlation store for <see cref="Component.InvokeJsAsync{T}(string)" />
///     round-trips. The host assigns each pending invocation a monotonic id, queues
///     it on the render payload, and stashes a callback here keyed by id. When the
///     client replies (server: WS <c>{type:"invokeResult"}</c> message; WASM:
///     <c>JSInterop.ResolveJsInvoke</c> JSExport), the host looks up the callback
///     and completes the awaiting <see cref="TaskCompletionSource{T}" />.
/// </summary>
internal static class JsInvokeResultStore
{
    private static int _counter;
    private static readonly ConcurrentDictionary<int, Action<JsonElement?, string?>> _pending = new();

    public static int Register(Action<JsonElement?, string?> callback)
    {
        // Int32 wraparound after 2B calls is fine — a TCS callback for an id
        // assigned 2B invokes ago has long since faulted on timeout (or completed),
        // and the next allocation finds an unused slot.
        var id = Interlocked.Increment(ref _counter);
        _pending[id] = callback;
        return id;
    }

    public static bool TryResolve(int id, JsonElement? result, string? error)
    {
        if (!_pending.TryRemove(id, out var cb))
        {
            return false;
        }

        try { cb(result, error); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Rask InvokeJsAsync callback for id={id} threw: {ex}");
        }

        return true;
    }

    public static void Cancel(int id)
    {
        if (_pending.TryRemove(id, out var cb))
        {
            try { cb(null, "cancelled"); }
            catch { }
        }
    }
}
