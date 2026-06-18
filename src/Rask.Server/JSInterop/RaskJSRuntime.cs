using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core.Live;

namespace Rask.Server.JSInterop;

/// <summary>
///     <see cref="JSRuntime" /> implementation backed by Rask's per-session WebSocket
///     transport. Lets any Blazor-style library that takes an <see cref="IJSRuntime" />
///     (e.g. <c>ProtectedSessionStorage</c>, <c>ProtectedLocalStorage</c>, community
///     wrappers like <c>Blazored.LocalStorage</c>) work on top of Rask unchanged.
///     <para>
///         The base <see cref="JSRuntime" /> class handles pending-task tracking, JSON
///         serialisation of args and return values (including <see cref="IJSObjectReference" />
///         and <see cref="DotNetObjectReference{T}" /> marshalling), and the JSON converters
///         that round-trip handle ids. This class only plugs in the transport:
///         <see cref="BeginInvokeJS(long, string, string?, JSCallResultType, long)" /> queues
///         the call onto the current <see cref="LiveSession" />'s pending interop list and
///         requests a render so the next outbound WS frame ships it;
///         <see cref="EndInvokeDotNet" /> ships a <c>[JSInvokable]</c> result back to the
///         client over the same channel.
///     </para>
///     <para>
///         <b>Trim safety:</b> the inherited <c>JsonSerializer.Deserialize&lt;TValue&gt;</c>
///         path is not trim-safe. Rask.Server is not published with linker trimming on
///         (only <c>Rask.Wasm</c> is), so no additional annotations are required here. A
///         future WASM <c>IJSRuntime</c> implementation will need DAM annotations or
///         user-side <c>JsonSerializerContext</c> registration.
///     </para>
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Server-side only; Rask.Server is not trim-published. " +
                    "Users of generic InvokeAsync<T> rooted via DAM on IJSRuntime.")]
internal sealed class RaskJSRuntime : RaskJSRuntimeBase
{
    private readonly LiveSessionAccessor _accessor;

    public RaskJSRuntime(LiveSessionAccessor accessor) => _accessor = accessor;

    // The shared base owns BeginInvokeJS (queue onto the session + request a render so the next
    // outbound frame's jsInvokes carries the call). This class only supplies the host seam: which
    // session is current, and how a [JSInvokable] result rides the WebSocket back.
    protected override ILiveJsHost CurrentHost =>
        _accessor.Session ?? throw new InvalidOperationException(
            "IJSRuntime can only be used within a Rask session scope. " +
            "Inject it through a Component ctor (DI) and call it from a lifecycle hook " +
            "(OnMountAsync, OnRenderedAsync) or event handler — not from a unit test or " +
            "app-level singleton.");

    // Server is frame-based even outside a render: queue the call and request a render so the next
    // outbound WS frame ships it; the mid-await render pump flushes that frame while a handler
    // awaits, so the awaiting ValueTask completes when the client's jsResult comes back.
    protected override void DispatchOutsideRender(PendingJsInvoke invoke)
    {
        var host = CurrentHost;
        host.JsInvokes.Enqueue(invoke);
        _ = host.RequestRenderAsync();
    }

    /// <summary>
    ///     Called by the base <see cref="JSRuntime" /> when JS invokes a <c>[JSInvokable]</c>
    ///     .NET method via <c>DotNet.invokeMethodAsync</c> and the result is ready. Ships
    ///     the result back to the client as a <c>dotNetResult</c> WS frame; the client-side
    ///     <c>DotNet</c> shim completes the JS-side promise.
    /// </summary>
    protected override void EndInvokeDotNet(
        DotNetInvocationInfo invocationInfo,
        in DotNetInvocationResult invocationResult)
    {
        var session = (LiveSession)CurrentHost;
        // invocationResult.ResultJson is already serialised against this runtime's
        // JsonSerializerOptions (including IJSObjectReference handle ids); we just package
        // it into a WS message and let the client-side DotNet shim parse it.
        var payload = BuildDotNetResultJson(
            invocationInfo.CallId,
            invocationResult.Success,
            invocationResult.Success ? invocationResult.ResultJson : null,
            invocationResult.Success
                ? null
                : invocationResult.Exception?.Message ?? "DotNet invocation failed",
            type: "dotNetResult");
        _ = session.SendOutOfBandAsync(payload);
    }
}
