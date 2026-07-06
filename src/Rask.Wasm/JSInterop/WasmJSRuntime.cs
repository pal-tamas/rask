using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core.Live;

namespace Rask.Wasm;

/// <summary>
///     <see cref="JSRuntime" /> implementation backed by the WASM <c>[JSImport]</c> /
///     <c>[JSExport]</c> bridge. Mirrors <c>Rask.Server.JSInterop.RaskJSRuntime</c>'s
///     contract: every <c>IJSRuntime.InvokeAsync</c> call lands in the base runtime's
///     <c>BeginInvokeJS</c>, which
///     hands the call to <c>rask.wasm.js</c>'s <c>dispatchJsInvoke</c>. Results return
///     through the <c>endInvokeJSResult</c> <c>[JSExport]</c> in
///     <see cref="JSInterop" /> (which calls <see cref="DotNetDispatcher.EndInvokeJS" />).
///     <para>
///         Trim safety: same caveat as <c>RaskJSRuntime</c> — base-class
///         <c>JsonSerializer.Deserialize&lt;TValue&gt;</c> isn't trim-safe. Users calling
///         <c>InvokeAsync&lt;ComplexType&gt;</c> on WASM must keep the type rooted (via
///         DAM on the call site or a <c>JsonSerializerContext</c>). Mirrors Blazor WASM.
///     </para>
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Forwards TValue's trim annotations from IJSRuntime.InvokeAsync<TValue>. " +
                    "Users must keep their TValue types rooted on WASM.")]
internal sealed class WasmJSRuntime : RaskJSRuntimeBase
{
    // Set once the session exists (it's built after this DI singleton — see WasmLiveSession ctor).
    // BeginInvokeJS (in the base) queues onto it; WasmLiveSession drains the queue into each frame.
    private ILiveJsHost? _host;

    public WasmJSRuntime()
    {
        // The base JSRuntime's JsonSerializerOptions ships with no TypeInfoResolver,
        // so Serialize / Deserialize<T> falls back to the runtime default. PublishTrimmed
        // apps (this includes Rask.Example.Wasm) flip
        // JsonSerializer.IsReflectionEnabledByDefault to false, and that fallback then
        // throws "JsonSerializerIsReflectionDisabled" on the very first
        // InvokeAsync<string> — including the built-in primitive case. Explicitly
        // chaining the reflection-based resolver here makes InvokeAsync<T> work for
        // any T the user (or framework) can keep rooted via DAM or a
        // JsonSerializerContext. Same model Blazor WASM ships with.
        //
        // Root the framework's own browser-API return types (e.g. GeolocationPosition from
        // IGeolocation) with their source-generated, trim-safe metadata ahead of the reflection
        // fallback — so they survive PublishTrimmed without the caller wiring up a context.
        JsonSerializerOptions.TypeInfoResolverChain.Add(Rask.Core.Browser.RaskBrowserJsonContext.Default);
        JsonSerializerOptions.TypeInfoResolverChain.Add(Browser.RaskWasmBrowserJsonContext.Default);
        JsonSerializerOptions.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
    }

    /// <summary>Bind the session this runtime queues calls onto (called from the session ctor).</summary>
    public void AttachHost(ILiveJsHost host) => _host = host;

    // The shared base queues calls made DURING a render (e.g. an OnRenderedAsync focus) onto this
    // host so they ship in the frame and run after applyDiff — the post-commit ordering that makes
    // WASM focus land like Server. Calls OUTSIDE a render go through DispatchOutsideRender below.
    protected override ILiveJsHost CurrentHost =>
        _host ?? throw new InvalidOperationException(
            "IJSRuntime can only be used within a Rask session scope. " +
            "Inject it through a Component ctor (DI) and call it from a lifecycle hook " +
            "(OnMountAsync, OnRenderedAsync) or event handler.");

    // Outside a render (a handler awaiting js.InvokeAsync), dispatch immediately through the JSImport
    // bridge — WASM's long-standing handler-interop path. The result returns via the EndInvokeJSResult
    // JSExport, completing the awaiting ValueTask without needing a render frame to flush it. taskId /
    // targetInstanceId travel as strings to dodge BigInt marshalling; the dispatcher rebuilds them.
    protected override void DispatchOutsideRender(PendingJsInvoke invoke) =>
        JSInterop.BeginInvokeJSImport(
            invoke.TaskId.ToString(),
            invoke.Identifier,
            invoke.ArgsJson,
            invoke.ResultType,
            invoke.TargetInstanceId.ToString());

    protected override void EndInvokeDotNet(
        DotNetInvocationInfo invocationInfo,
        in DotNetInvocationResult invocationResult)
    {
        // Ship a [JSInvokable] call's result back to the JS-side `DotNet` shim via the dedicated
        // endDotNetInvoke JSImport — no `type` discriminator needed (unlike the Server's multiplexed WS).
        var payload = BuildDotNetResultJson(
            invocationInfo.CallId,
            invocationResult.Success,
            invocationResult.Success ? invocationResult.ResultJson : null,
            invocationResult.Success
                ? null
                : invocationResult.Exception?.Message ?? "DotNet invocation failed");
        JSInterop.EndDotNetInvokeImport(Encoding.UTF8.GetString(payload));
    }
}
