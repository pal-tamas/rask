using System.Diagnostics.CodeAnalysis;
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
        // source-generated, trim-safe metadata, then add the reflection resolver whenever reflection-based
        // JSON is enabled — which also handles the IJSRuntime call args (a plain object[] that can't be
        // source-generated). The guard is JsonSerializer.IsReflectionEnabledByDefault, NOT
        // RuntimeFeature.IsDynamicCodeSupported: iOS reports IsDynamicCodeSupported == false even on the
        // simulator / interpreter (breaking every invoke-with-args), whereas IsReflectionEnabledByDefault
        // is the exact predicate DefaultJsonTypeInfoResolver needs and is trim-substituted to false under a
        // full-AOT publish (so the branch is removed and a user calling InvokeAsync<TCustom> then supplies a
        // JsonSerializerContext).
        JsonSerializerOptions.TypeInfoResolverChain.Add(RaskBrowserJsonContext.Default);
        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            JsonSerializerOptions.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        }
    }

    internal const string NoJavaScriptEngineMessage =
        "This app is running pure-native (NativeAppHost.RunNativeAsync), so there is no WebView and no "
        + "JavaScript engine for IJSRuntime to call into. Use a native capability instead (IShare, "
        + "IGeolocation, …), or boot with RunLocalAsync and host a NativeWebView on the route that needs JS.";

    /// <summary>Bind the session (for render-time queuing) and the WebView (for out-of-render dispatch).</summary>
    /// <remarks>
    ///     <paramref name="webView" /> is <see langword="null" /> in the pure-native model
    ///     (<c>NativeAppHost.RunNativeAsync</c>), where there is no JS engine at all. The session still
    ///     attaches so that a call gets the accurate error below rather than "not in a session scope",
    ///     which would send somebody looking for a lifecycle-hook problem they do not have (#777).
    /// </remarks>
    public void AttachHost(ILiveJsHost host, INativeWebView? webView)
    {
        _host = host;
        _webView = webView;
    }

    protected override ILiveJsHost CurrentHost
    {
        get
        {
            if (_host is null)
            {
                throw new InvalidOperationException(
                    "IJSRuntime can only be used within a Rask session scope. " +
                    "Inject it through a Component ctor (DI) and call it from a lifecycle hook " +
                    "(OnMountAsync, OnRenderedAsync) or event handler.");
            }

            if (_webView is null)
            {
                throw new InvalidOperationException(NoJavaScriptEngineMessage);
            }

            return _host;
        }
    }

    // Outside a render, evaluate the invoke immediately in the WebView. window.__raskNative.beginInvokeJS
    // mirrors the WASM bridge's dispatchJsInvoke: it runs the identified function against argsJson and posts
    // a {type:'jsResult', ...} message back, which NativeAppHost feeds to DotNetDispatcher.EndInvokeJS. taskId
    // / targetInstanceId travel as strings (JS numbers can't hold the full range), matching the WASM host.
    protected override void DispatchOutsideRender(PendingJsInvoke invoke)
    {
        if (_webView is null)
        {
            // Dropping this would leave the caller's await pending for ever. Throwing reaches them.
            throw new InvalidOperationException(NoJavaScriptEngineMessage);
        }

        _ = _webView.EvaluateJavaScriptAsync(
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
    }

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
