using System.Text;
using Microsoft.Playwright;
using Rask.Native;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     An <see cref="INativeWebView" /> backed by a real (headless) Playwright page. It runs the ACTUAL
///     <c>rask.native.js</c> client + <see cref="NativeAppHost" /> render→diff→bridge pipeline inside
///     Chromium — the same WebView engine class Android ships — so the Native + Local host is E2E-tested
///     headlessly, with no emulator/simulator. It's the browser-backed sibling of the unit tests'
///     <c>FakeNativeWebView</c>.
///     <para>
///         Direction of travel:
///         <list type="bullet">
///             <item>.NET → page: <see cref="ApplyRenderAsync" /> pushes a frame into
///             <c>window.__raskNative.applyRender</c>; <see cref="EvaluateJavaScriptAsync" /> runs interop
///             JS. Both are serialized onto Playwright via <see cref="_gate" /> (Playwright pages are not
///             re-entrant).</item>
///             <item>page → .NET: an <c>ExposeFunctionAsync("__raskSend", …)</c> binding routes the
///             client's <c>window.__raskSend(json)</c> into <see cref="OnMessage" />.</item>
///         </list>
///     </para>
/// </summary>
internal sealed class PlaywrightNativeWebView : INativeWebView
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPage _page;

    // Serializes the START of message processing so messages are delivered to OnMessage in the exact order
    // the client posted them — a real WKScriptMessageHandler / JavascriptInterface delivers them in order on
    // the UI thread, and out-of-order delivery would let a `click` outrun the coalesced `input` it depends on
    // (the handler would then read a stale value). Each link starts the handler and returns WITHOUT awaiting
    // its completion, so a dispatch that parks awaiting a jsResult still lets the following jsResult message
    // run (mirrors the UI thread pumping the next message while a handler is suspended).
    private Task _deliverChain = Task.CompletedTask;

    private PlaywrightNativeWebView(IPage page) => _page = page;

    public Func<byte[], Task>? OnMessage { get; set; }

    public async ValueTask ApplyRenderAsync(ReadOnlyMemory<byte> frameUtf8)
    {
        // Copy to a string now — the session reuses its write buffer after this returns.
        var json = Encoding.UTF8.GetString(frameUtf8.Span);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _page.EvaluateAsync(
                "j => { if (window.__raskNative) window.__raskNative.applyRender(j); }", json)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask EvaluateJavaScriptAsync(string javaScript)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _page.EvaluateAsync(javaScript).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Installs the client → host bridge on the page. MUST be called before the page navigates so the
    ///     client's boot <c>{"type":"ready"}</c> post isn't dropped. The binding is fire-and-forget: it
    ///     hands the message to <see cref="OnMessage" /> on the thread pool and returns immediately, so the
    ///     host's follow-up <see cref="ApplyRenderAsync" /> (which calls back into the page) can't deadlock
    ///     the Playwright binding dispatcher.
    /// </summary>
    public static async Task<PlaywrightNativeWebView> CreateAsync(IPage page)
    {
        var view = new PlaywrightNativeWebView(page);
        await page.ExposeFunctionAsync<string>("__raskSend", json =>
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            // Chain the STARTS in delivery order (the continuation kicks off the next handler only after the
            // previous one has started), but fire-and-forget each so a parked dispatch doesn't stall the pump.
            view._deliverChain = view._deliverChain.ContinueWith(
                prev => { _ = view.OnMessage?.Invoke(bytes); },
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        });
        return view;
    }
}
