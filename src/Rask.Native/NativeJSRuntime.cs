using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core.Browser;
using Rask.Core.Live;

namespace Rask.Native;

/// <summary>
///     <see cref="JSRuntime" /> backed by the native WebView bridge. Mirrors
///     <c>Rask.Wasm.WasmJSRuntime</c>: an <c>IJSRuntime.InvokeAsync</c> made DURING a render queues onto
///     the session and ships in the frame (the client runs it after <c>applyDiff</c>); a call made OUTSIDE
///     a render (a handler awaiting <c>js.InvokeAsync</c>) is evaluated immediately in the WebView via
///     <see cref="INativeWebView.EvaluateJavaScriptAsync" />, and its result returns as a <c>jsResult</c>
///     message routed back through <see cref="DotNetDispatcher.EndInvokeJS" /> by <see cref="NativeAppHost" />.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Forwards TValue's trim annotations from IJSRuntime.InvokeAsync<TValue>. " +
                    "Users must keep their TValue types rooted on native (DAM or a JsonSerializerContext).")]
internal sealed class NativeJSRuntime : RaskJSRuntimeBase
{
    private ILiveJsHost? _host;
    private INativeWebView? _webView;

    public NativeJSRuntime()
    {
        // Root the framework's own browser-API return types (e.g. GeolocationPosition) with their
        // source-generated, trim-safe metadata, then add the reflection resolver only when the runtime
        // can generate code — same model as WasmJSRuntime. Under a full-AOT iOS publish the reflection
        // branch is trimmed away and a user calling InvokeAsync<TCustom> must supply a JsonSerializerContext.
        JsonSerializerOptions.TypeInfoResolverChain.Add(RaskBrowserJsonContext.Default);
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            JsonSerializerOptions.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        }
    }

    /// <summary>Bind the session (for render-time queuing) and the WebView (for out-of-render dispatch).</summary>
    public void AttachHost(ILiveJsHost host, INativeWebView webView)
    {
        _host = host;
        _webView = webView;
    }

    protected override ILiveJsHost CurrentHost =>
        _host ?? throw new InvalidOperationException(
            "IJSRuntime can only be used within a Rask session scope. " +
            "Inject it through a Component ctor (DI) and call it from a lifecycle hook " +
            "(OnMountAsync, OnRenderedAsync) or event handler.");

    // Outside a render, evaluate the invoke immediately in the WebView. window.__raskNative.beginInvokeJS
    // mirrors the WASM bridge's dispatchJsInvoke: it runs the identified function against argsJson and posts
    // a {type:'jsResult', ...} message back, which NativeAppHost feeds to DotNetDispatcher.EndInvokeJS. taskId
    // / targetInstanceId travel as strings (JS numbers can't hold the full range), matching the WASM host.
    protected override void DispatchOutsideRender(PendingJsInvoke invoke) =>
        _ = _webView?.EvaluateJavaScriptAsync(
            "window.__raskNative.beginInvokeJS(" +
            invoke.TaskId.ToString() + "," +
            Quote(invoke.Identifier) + "," +
            // argsJson must arrive as a STRING for the client's JSON.parse(argsJson) — the same shape the
            // frame-invoke path ships it as. Embedding it raw would hand beginInvokeJS a JS array/object
            // literal (JSON.parse then chokes on the coerced "a,b" text), breaking every out-of-render
            // IJSRuntime call that carries arguments (sessionStorage, element-ref focus, …).
            (invoke.ArgsJson is null ? "null" : Quote(invoke.ArgsJson)) + "," +
            (int)invoke.ResultType + "," +
            Quote(invoke.TargetInstanceId.ToString()) + ")");

    protected override void EndInvokeDotNet(DotNetInvocationInfo invocationInfo, in DotNetInvocationResult invocationResult)
    {
        var payload = BuildDotNetResultJson(
            invocationInfo.CallId,
            invocationResult.Success,
            invocationResult.Success ? invocationResult.ResultJson : null,
            invocationResult.Success ? null : invocationResult.Exception?.Message ?? "DotNet invocation failed");
        _ = _webView?.EvaluateJavaScriptAsync(
            "window.__raskNative.endDotNetInvoke(" + Quote(Encoding.UTF8.GetString(payload)) + ")");
    }

    // Trim-safe JSON string literal (no reflection): escapes then wraps in quotes so the value can be
    // embedded straight into the evaluated JS call.
    private static string Quote(string value) => "\"" + JsonEncodedText.Encode(value) + "\"";
}
