using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core.Diagnostics;

namespace Rask.Core.Live;

// The per-session queue of pending IJSRuntime.InvokeAsync calls, shared by both hosts. A call lands
// here via RaskJSRuntimeBase.BeginInvokeJS during a render (or an event handler); the session's
// payload builder drains it into the outbound frame's `jsInvokes`, and the client dispatches each
// AFTER it applies the render's DOM patch. That post-commit ordering is the whole point: interop
// issued from a lifecycle hook (e.g. OnRenderedAsync focusing a dialog as it opens) must run against
// the committed DOM, not the pre-patch one. Server always worked this way; routing WASM through the
// same queue gives it the same ordering instead of dispatching immediately (pre-patch).
internal sealed class LiveJsInvokeQueue
{
    private readonly List<PendingJsInvoke> _pending = new();

    public void Enqueue(PendingJsInvoke invoke)
    {
        lock (_pending)
        {
            _pending.Add(invoke);
        }
    }

    public bool HasPending
    {
        get
        {
            lock (_pending)
            {
                return _pending.Count > 0;
            }
        }
    }

    /// <summary>Snapshot and clear the queue, or null when empty.</summary>
    public PendingJsInvoke[]? Drain()
    {
        lock (_pending)
        {
            if (_pending.Count == 0)
            {
                return null;
            }

            var invokes = _pending.ToArray();
            _pending.Clear();
            return invokes;
        }
    }

    /// <summary>
    ///     Complete the awaiting <see cref="ValueTask{T}" />s with a failure when the frame carrying
    ///     these invokes never reached the client (e.g. the WebSocket send threw). Without this the
    ///     caller's <c>await js.InvokeAsync(...)</c> would hang forever.
    /// </summary>
    public static void Fail(JSRuntime runtime, PendingJsInvoke[] invokes, string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            message = "Rask: render frame carrying the JS invoke was never delivered";
        }

        foreach (var invoke in invokes)
        {
            try
            {
                using var stream = new MemoryStream(128);
                using (var writer = new Utf8JsonWriter(stream))
                {
                    // Canonical EndInvokeJS triple: [taskId, success=false, error].
                    writer.WriteStartArray();
                    writer.WriteNumberValue(invoke.TaskId);
                    writer.WriteBooleanValue(false);
                    writer.WriteStringValue(message);
                    writer.WriteEndArray();
                }

                DotNetDispatcher.EndInvokeJS(runtime, Encoding.UTF8.GetString(stream.ToArray()));
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error,
                    "Rask.JsInvoke",
                    $"Rask: failed to surface JS invoke fault for taskId={invoke.TaskId}",
                    ex);
            }
        }
    }
}

// Implemented by both LiveSession (Server) and WasmLiveSession (WASM) so RaskJSRuntimeBase can queue
// a call and request a render without knowing the transport.
internal interface ILiveJsHost
{
    LiveJsInvokeQueue JsInvokes { get; }

    Task RequestRenderAsync();

    /// <summary>
    ///     Record that the page needs a live connection. Defaulted to a no-op so a host that has no
    ///     notion of a static render (or a test double) is unaffected.
    /// </summary>
    void MarkRequiresLiveSession(InteractivityReason reason)
    {
    }
}
