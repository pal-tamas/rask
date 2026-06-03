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
internal sealed class RaskJSRuntime : JSRuntime
{
    private readonly LiveSessionAccessor _accessor;

    public RaskJSRuntime(LiveSessionAccessor accessor)
    {
        _accessor = accessor;
    }

    private LiveSession CurrentSession =>
        _accessor.Session ?? throw new InvalidOperationException(
            "IJSRuntime can only be used within a Rask session scope. " +
            "Inject it through a Component ctor (DI) and call it from a lifecycle hook " +
            "(OnMountAsync, OnRenderedAsync) or event handler — not from a unit test or " +
            "app-level singleton.");

    /// <summary>
    ///     Called by the base <see cref="JSRuntime" /> for every <c>InvokeAsync&lt;T&gt;</c>
    ///     and <c>InvokeVoidAsync</c>. Queues the call onto the current session's pending
    ///     list and triggers a render — the next outbound frame's <c>jsInvokes</c> array
    ///     carries the payload to the client, which resolves the dotted identifier on
    ///     <c>window</c>, invokes it, and ships the result back as a <c>jsResult</c>
    ///     message. The base class completes the awaiting <see cref="ValueTask{T}" /> when
    ///     <see cref="JSRuntime.EndInvokeJS(string)" /> fires from
    ///     <c>RaskEndpointExtensions.HandleJsResult</c>.
    /// </summary>
    protected override void BeginInvokeJS(
        long taskId,
        string identifier,
        string? argsJson,
        JSCallResultType resultType,
        long targetInstanceId)
    {
        var session = CurrentSession;
        var invoke = new PendingJsInvoke(taskId, identifier, argsJson, (int)resultType, targetInstanceId);
        session.EnqueueJsInvoke(invoke);
        // Skip RequestRenderAsync when we're already mid-render: the current frame's
        // payload builder drains _pendingJsInvokes after the walk, so the invoke ships
        // on this frame anyway. Requesting another render here would re-fire every
        // lifecycle hook (most notably OnRenderedAsync), which would call into us
        // again → infinite render loop. Check IsActive rather than just Current — an
        // async continuation that captured a ctx via AsyncLocal still observes Current
        // after the walk disposed; only the live walk's drain will pick up our invoke.
        if (LiveRenderContext.Current is { IsActive: true })
        {
            return;
        }

        // Fire and forget: the ValueTask returned to the caller (via the base class's
        // TCS) completes when the client's jsResult message comes back, regardless of
        // how the render cycle interleaves.
        _ = session.RequestRenderAsync();
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
        var session = CurrentSession;
        // invocationResult.ResultJson is already serialised against this runtime's
        // JsonSerializerOptions (including IJSObjectReference handle ids); we just package
        // it into a WS message and let the client-side DotNet shim parse it.
        var payload = BuildDotNetResultPayload(
            invocationInfo.CallId,
            invocationResult.Success,
            invocationResult.Success ? invocationResult.ResultJson : null,
            invocationResult.Success
                ? null
                : invocationResult.Exception?.Message ?? "DotNet invocation failed");
        _ = session.SendOutOfBandAsync(payload);
    }

    private static byte[] BuildDotNetResultPayload(string? callId, bool success, string? resultJson, string? error)
    {
        using var stream = new MemoryStream(128);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "dotNetResult");
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
