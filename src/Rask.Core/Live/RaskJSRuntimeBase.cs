using System.Text.Json;
using Microsoft.JSInterop;

namespace Rask.Core.Live;

// Shared base for both hosts' IJSRuntime implementations (Rask.Server's RaskJSRuntime,
// Rask.Wasm's WasmJSRuntime). The base owns the transport-independent contract: every
// InvokeAsync/InvokeVoidAsync lands in BeginInvokeJS, which queues the call onto the current
// session and asks for a render so the next outbound frame ships it to the client (where it runs
// AFTER applyDiff). Subclasses supply the host seam — which session is current and how a
// [JSInvokable] result is shipped back — plus any host-specific serializer setup.
internal abstract class RaskJSRuntimeBase : JSRuntime
{
    /// <summary>
    ///     The session this call belongs to. Throws when used outside a Rask session scope (e.g.
    ///     from a unit test or an app-level singleton rather than a component lifecycle hook).
    /// </summary>
    protected abstract ILiveJsHost CurrentHost { get; }

    protected override void BeginInvokeJS(
        long taskId,
        string identifier,
        string? argsJson,
        JSCallResultType resultType,
        long targetInstanceId)
    {
        var invoke = new PendingJsInvoke(taskId, identifier, argsJson, (int)resultType, targetInstanceId);

        // A page that calls into JavaScript needs the connection that carries the call. Marked via
        // the render context, NOT CurrentHost: that property throws when there is no session, and
        // reaching for it here made an interop call outside one fail before it could dispatch.
        // The context is the right scope regardless — a call made during the initial render is
        // exactly what makes that page interactive, and a call made outside one has no initial
        // render left to classify.
        LiveRenderContext.CurrentSync?.MarkRequiresLiveSession(InteractivityReason.JsInterop);

        // Mid-render (e.g. an OnRenderedAsync hook focusing a dialog as it opens): queue onto the
        // current frame so the client runs it AFTER applyDiff — i.e. against the committed DOM. This
        // is the shared, transport-independent half, and the reason WASM focus now lands like Server.
        // Don't request another render: the in-flight frame's builder drains the queue after the
        // walk, and re-rendering here would re-fire lifecycle hooks → an unbounded loop. IsActive
        // (not just Current) matters — an async continuation that captured a ctx via AsyncLocal still
        // observes Current after the walk disposed; only the live walk's drain picks up our invoke.
        if (LiveRenderContext.Current is { IsActive: true })
        {
            CurrentHost.JsInvokes.Enqueue(invoke);
            return;
        }

        // Outside a render (an event handler awaiting js.InvokeAsync): the hosts diverge. Server
        // queues it and requests a render — its mid-await render pump flushes the frame so the
        // awaiting ValueTask completes. WASM dispatches it immediately through the JSImport bridge,
        // the same path it has always used for handler interop (the DOM is already committed from
        // the prior frame, and an immediate round-trip is what unblocks the awaiting handler).
        DispatchOutsideRender(invoke);
    }

    /// <summary>Handle a call issued outside a render walk — see <see cref="BeginInvokeJS" />.</summary>
    protected abstract void DispatchOutsideRender(PendingJsInvoke invoke);

    /// <summary>
    ///     Build the <c>{ callId, success, result?, error? }</c> envelope a <c>[JSInvokable]</c>
    ///     result is shipped back in. Shared by both hosts; only the transport (WS out-of-band send
    ///     vs the WASM <c>endDotNetInvoke</c> JSImport) differs, so subclasses call this from
    ///     <see cref="JSRuntime.EndInvokeDotNet" /> and forward the bytes their own way.
    ///     <paramref name="type" /> tags the envelope for hosts that multiplex many message kinds
    ///     over one channel (Server's WS sends <c>type:"dotNetResult"</c>); WASM passes null because
    ///     its dedicated <c>endDotNetInvoke</c> JSImport needs no discriminator.
    /// </summary>
    protected static byte[] BuildDotNetResultJson(
        string? callId, bool success, string? resultJson, string? error, string? type = null)
    {
        using var stream = new MemoryStream(128);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (type is not null)
            {
                writer.WriteString("type", type);
            }

            if (callId is not null)
            {
                writer.WriteString("callId", callId);
            }

            writer.WriteBoolean("success", success);
            if (resultJson is not null)
            {
                writer.WritePropertyName("result");
                // Pre-serialised JSON — write as a raw value so we don't double-encode.
                using var doc = JsonDocument.Parse(resultJson);
                doc.RootElement.WriteTo(writer);
            }

            if (error is not null)
            {
                writer.WriteString("error", error);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
