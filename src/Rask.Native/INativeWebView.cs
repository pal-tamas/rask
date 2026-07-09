namespace Rask.Native;

/// <summary>
///     The platform WebView seam. <see cref="NativeAppHost" /> drives the render → diff pipeline and
///     pushes frames through this bridge; the platform head (the <c>rask-native</c> template's app
///     project, which multi-targets <c>net10.0-ios</c>/<c>net10.0-android</c>) implements it over the
///     concrete control — a <c>WKWebView</c> on iOS, an <c>android.webkit.WebView</c> on Android.
///     <para>
///         Contract: <see cref="ApplyRenderAsync" /> hands a rendered frame (a UTF-8 JSON envelope,
///         exactly what <c>rask-dom.js</c>'s <c>applyDiff</c> / <c>rask-morph.js</c>'s <c>morph</c>
///         consume) to the WebView's <c>window.__raskNative.applyRender</c>. <see cref="OnMessage" /> is
///         invoked by the platform whenever the WebView posts an event/jsResult/dotNetInvoke back to
///         .NET (via <c>WKScriptMessageHandler</c> / a <c>[JavascriptInterface]</c> method). Both sides
///         speak the same wire format the WASM host uses over its JSExport <c>Dispatch</c> boundary.
///     </para>
///     Implementations MUST marshal <see cref="ApplyRenderAsync" /> and <see cref="EvaluateJavaScriptAsync" />
///     onto the platform UI thread (WebView JS evaluation is UI-thread-affine); the render pipeline runs
///     off it.
/// </summary>
public interface INativeWebView
{
    /// <summary>
    ///     Push one rendered frame (UTF-8 JSON) into the WebView, where the client runtime applies it
    ///     (diff ops via <c>applyDiff</c>, or a full-HTML <c>morph</c>). Called from
    ///     <c>NativeLiveSession.SendFrameAsync</c>. The memory is only valid for the duration of the
    ///     call — copy if the platform hop is asynchronous past the await.
    /// </summary>
    ValueTask ApplyRenderAsync(ReadOnlyMemory<byte> frameUtf8);

    /// <summary>
    ///     Evaluate JavaScript in the WebView. Used by <c>NativeJSRuntime</c> to ship an
    ///     <see cref="Microsoft.JSInterop.IJSRuntime" /> call the app makes outside a render (a handler
    ///     awaiting <c>js.InvokeAsync</c>) into the page; the result returns through
    ///     <see cref="OnMessage" /> as a <c>jsResult</c> message.
    /// </summary>
    ValueTask EvaluateJavaScriptAsync(string javaScript);

    /// <summary>
    ///     Set by the host to receive JS → .NET messages: component events (<c>click</c>/<c>input</c>/
    ///     <c>submit</c>/<c>navigate</c>/…), <c>jsResult</c> replies, and <c>dotNetInvoke</c> calls — each
    ///     a UTF-8 JSON payload. The platform head calls this from its script-message handler.
    /// </summary>
    Func<byte[], Task>? OnMessage { get; set; }
}
