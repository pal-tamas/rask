#if RASK_BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core.Diagnostics;
using Rask.Core.Routing;
using Rask.Wasm.Files;

namespace Rask.Wasm;

// `partial` is required by the JSImport source generator (it emits the
// `[JSImport]` method bodies into a second partial declaration). Rider's
// non-browser view doesn't see the generated counterpart and would otherwise
// flag the modifier as redundant — see .editorconfig for the inspection
// suppression. Removing `partial` breaks the WASM build with CS0751.
internal static partial class JSInterop
{
    private const string ModuleName = "rask";
    private static WasmLiveSession? _session;
    private static WasmJSRuntime? _runtime;
    private static WasmHostedServices? _hostedServices;

    /// <summary>How long a hosted service gets to stop once the page is going away.</summary>
    /// <remarks>
    ///     Short on purpose. The browser does not await a <c>pagehide</c> handler, so this bounds an
    ///     unloading tab's work rather than promising it — see <see cref="WasmHostedServices.StopAsync" />.
    /// </remarks>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(2);

    public static void Init(WasmLiveSession session) => _session = session;

    /// <summary>
    ///     Bind the app's hosted services so <c>StopHostedServices</c> can drain them when the page
    ///     unloads. Called once from <c>WasmHostBuilder.RunAsync</c>. Referenced by name, not by
    ///     <c>cref</c>: the export it names only exists on the browser TFM.
    /// </summary>
    public static void Init(WasmHostedServices hostedServices) => _hostedServices = hostedServices;

    /// <summary>
    ///     Bind the singleton <see cref="WasmJSRuntime" /> so the <c>[JSExport]</c>
    ///     entry points below can route inbound results / DotNet invocations to it.
    ///     Called once from <c>WasmHostBuilder.RunAsync</c> after the DI container
    ///     resolves the runtime.
    /// </summary>
    public static void Init(WasmJSRuntime runtime) => _runtime = runtime;

#if RASK_BROWSER
    public static Task ImportJsModuleAsync() =>
        JSHost.ImportAsync(ModuleName, "../rask.wasm.js");

    [JSExport]
    public static async Task Dispatch(byte[] json)
    {
        if (_session is null) return;

        // DispatchAsync builds the frame and pushes it to JS itself — zero-copy via applyRender
        // (a MemoryView over its write buffer), with a double-buffered dedup. There is nothing to
        // apply here; the byte[] it returns is retained only as a unit-test seam.
        await _session.DispatchAsync(json).ConfigureAwait(false);
    }

    /// <summary>
    ///     Inbound result for an <c>IJSRuntime.InvokeAsync</c> call. <paramref name="arguments" />
    ///     is the canonical <c>[taskId, success, result|error]</c> triple that
    ///     <see cref="DotNetDispatcher.EndInvokeJS" /> parses to complete the
    ///     awaiting <c>ValueTask&lt;T&gt;</c>.
    /// </summary>
    [JSExport]
    public static void EndInvokeJSResult(string arguments)
    {
        if (_runtime is null) return;
        try
        {
            DotNetDispatcher.EndInvokeJS(_runtime, arguments);
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error,
                "Rask.Wasm",
                "[Rask.Wasm] EndInvokeJSResult dispatch failed",
                ex);
        }
    }

    /// <summary>
    ///     JS-initiated <c>DotNet.invokeMethodAsync</c> entry point. Hands the call to
    ///     <see cref="DotNetDispatcher.BeginInvokeDotNet" />; the runtime completes the
    ///     call asynchronously and <see cref="WasmJSRuntime.EndInvokeDotNet" /> ships
    ///     the result back via <see cref="EndDotNetInvokeImport" />.
    /// </summary>
    [JSExport]
    public static void BeginDotNetInvoke(
        string callId,
        string? assemblyName,
        string methodIdentifier,
        // Travels as int — DotNetObjectReference handle ids are minted by the JSRuntime
        // base class and bounded well below int.MaxValue in any realistic workload.
        // Sidesteps SYSLIB1072 (JSExport doesn't marshal long without an explicit
        // JSMarshalAsAttribute, which the source generator otherwise rejects).
        int dotNetObjectId,
        string argsJson)
    {
        if (_runtime is null) return;
        try
        {
            var info = new DotNetInvocationInfo(assemblyName, methodIdentifier, dotNetObjectId, callId);
            DotNetDispatcher.BeginInvokeDotNet(_runtime, info, argsJson);
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error,
                "Rask.Wasm",
                $"[Rask.Wasm] BeginDotNetInvoke '{assemblyName}.{methodIdentifier}' failed",
                ex);
        }
    }

    /// <summary>
    ///     Drains the app's hosted services because the page is going away. Called from the
    ///     <c>pagehide</c> listener in <c>rask.wasm.js</c> — only for a real teardown, never for a
    ///     back/forward-cache suspend, where the page can be resumed with its services still needed.
    /// </summary>
    [JSExport]
    public static Task StopHostedServices() =>
        _hostedServices?.StopAsync(ShutdownGrace) ?? Task.CompletedTask;

    // Sync byte[] return — JSExport's marshaller maps that to a Uint8Array on the JS side
    // with zero base64 round-trip. JS triggerDownload calls this in response to a render
    // payload that carried `download.token`, then synthesises the <a download> click against
    // the returned Uint8Array. The token is consumed on first pull; double-clicks yield
    // an empty array rather than throwing.
    [JSExport]
    public static byte[] PullDownload(string token)
    {
        if (_session is null || string.IsNullOrEmpty(token)) return Array.Empty<byte>();
        var sink = _session.Services.GetService<IDownloadSink>() as WasmDownloadSink;
        return sink?.Pull(token) ?? Array.Empty<byte>();
    }

    [JSImport("setExports", ModuleName)]
    public static partial void SetExports(JSObject exports);

    // MemoryView marshals a zero-copy view over the caller's buffer (no byte[] per frame); the JS
    // applyRender reads it synchronously within this call.
    [JSImport("applyRender", ModuleName)]
    public static partial void ApplyRender([JSMarshalAs<JSType.MemoryView>] Span<byte> payload);

    /// <summary>
    ///     Dev-only. Shows the "hot reload applied" indicator once the coordinator has finished
    ///     applying an update and every open session has repainted — the WASM analogue of the Server's
    ///     out-of-band <c>hotReload</c> frame, which a WASM app has no server to receive.
    /// </summary>
    [JSImport("hotReloadApplied", ModuleName)]
    public static partial void HotReloadApplied();

    /// <summary>
    ///     Shows a boot failure on the page. Called when startup throws before anything has rendered,
    ///     where the root error boundary cannot help — it needs a mounted tree to render its fallback
    ///     into, and there is none yet.
    /// </summary>
    /// <remarks>
    ///     Reported from managed code rather than left to the JS side because only this side has the
    ///     exception: to JS a startup failure is an opaque rejected promise out of <c>runMain</c>.
    /// </remarks>
    [JSImport("bootFailed", ModuleName)]
    public static partial void BootFailed(string message, string? detail);

    [JSImport("getLocation", ModuleName)]
    public static partial string GetLocation();

    [JSImport("getBaseAddress", ModuleName)]
    public static partial string GetBaseAddress();

    /// <summary>
    ///     The visitor's language signals — <c>?culture=</c>, the culture cookie and
    ///     <c>navigator.languages</c> — as one JSON object.
    /// </summary>
    /// <remarks>
    ///     Synchronous, and all three in a single call, deliberately. The culture has to be settled
    ///     BEFORE the first render, and an async probe would either delay the first paint or let the app
    ///     paint in the wrong language and correct itself in a second frame the visitor would see. That
    ///     also rules out <c>INavigatorInfo.LanguageAsync()</c>, which is async by design — and on the
    ///     Server host is a socket round trip.
    /// </remarks>
    [JSImport("getCultureSignals", ModuleName)]
    public static partial string GetCultureSignals();

    /// <summary>
    ///     Browser sub-path prefix derived from <c>&lt;base href&gt;</c>. Returns the
    ///     directory portion (e.g. <c>"/Rask/"</c> when hosted at <c>/Rask/index.html</c>
    ///     or <c>"/"</c> at the origin root). <see cref="WasmHostBuilder.CreateDefault()" />
    ///     uses this to seed <see cref="Rask.Core.Live.RaskLiveOptions.PathBase" /> so head-emitted asset
    ///     URLs honour the deployment sub-path without explicit configuration.
    /// </summary>
    [JSImport("getBasePath", ModuleName)]
    public static partial string GetBasePath();

    [JSImport("pushHistory", ModuleName)]
    public static partial void PushHistory(string url, bool replace);

    [JSImport("readFileChunk", ModuleName)]
    public static partial Task<string> ReadFileChunkBase64Async(string @ref, int offset, int length);

    /// <summary>
    ///     Ship an IJSRuntime call to JS for dispatch. The id and target-instance values
    ///     travel as strings to avoid BigInt marshalling — both fit easily in JS Numbers
    ///     for any realistic call count.
    /// </summary>
    [JSImport("beginInvokeJS", ModuleName)]
    public static partial void BeginInvokeJSImport(
        string taskId,
        string identifier,
        string? argsJson,
        int resultType,
        string targetInstanceId);

    /// <summary>
    ///     Ship a <c>[JSInvokable]</c> .NET call's result to the JS-side <c>DotNet</c>
    ///     shim. <paramref name="resultJson" /> is a fully-serialised
    ///     <c>{ callId, success, result?, error? }</c> envelope.
    /// </summary>
    [JSImport("endDotNetInvoke", ModuleName)]
    public static partial void EndDotNetInvokeImport(string resultJson);

    public static async Task<byte[]> ReadFileChunkAsync(string @ref, int offset, int length)
    {
        var b64 = await ReadFileChunkBase64Async(@ref, offset, length).ConfigureAwait(false);
        return string.IsNullOrEmpty(b64) ? Array.Empty<byte>() : Convert.FromBase64String(b64);
    }
#else
    // Non-browser stubs. Used by the test project so the pure-logic code paths can be exercised
    // without a JS runtime. None of the non-browser stubs perform real interop.
    public static Task ImportJsModuleAsync() => Task.CompletedTask;

    public static Task Dispatch(byte[] json)
    {
        // Non-browser stub: drop the return value (matches the JSExport's Task return).
        // Tests call session.DispatchAsync(json) directly to inspect the byte[] payload.
        return _session?.DispatchAsync(json) ?? Task.CompletedTask;
    }

    public static void ApplyRender(Span<byte> payload) { }
    public static string GetLocation() => "/";
    public static string GetBaseAddress() => "/";

    /// <summary>No browser, so no signals — the app falls back to its configured default culture.</summary>
    public static string GetCultureSignals() => "{}";
    public static void PushHistory(string url, bool replace) { }

    public static string GetBasePath() => "/";

    /// <summary>Counts the indicator calls so the non-browser tests can assert the bridge fired.</summary>
    public static int HotReloadAppliedCount { get; private set; }

    public static void HotReloadApplied() => HotReloadAppliedCount++;

    internal static void ResetHotReloadAppliedCount() => HotReloadAppliedCount = 0;

    /// <summary>Records the last boot failure so the non-browser tests can assert what was reported.</summary>
    public static (string Message, string? Detail)? LastBootFailure { get; private set; }

    public static void BootFailed(string message, string? detail) => LastBootFailure = (message, detail);

    internal static void ResetBootFailure() => LastBootFailure = null;

    public static Task<byte[]> ReadFileChunkAsync(string @ref, int offset, int length) =>
        Task.FromResult(Array.Empty<byte>());

    // Records the last BeginInvokeJSImport call so tests can assert dispatch shape.
    public record BeginInvokeJsCall(
        string TaskId,
        string Identifier,
        string? ArgsJson,
        int ResultType,
        string TargetInstanceId);

    public static BeginInvokeJsCall? LastBeginInvokeJsCall { get; private set; }

    public static void BeginInvokeJSImport(string taskId, string identifier, string? argsJson, int resultType,
        string targetInstanceId) =>
        LastBeginInvokeJsCall = new BeginInvokeJsCall(taskId, identifier, argsJson, resultType, targetInstanceId);

    public static string? LastEndDotNetInvoke { get; private set; }
    public static void EndDotNetInvokeImport(string resultJson) => LastEndDotNetInvoke = resultJson;

    public static void EndInvokeJSResult(string arguments)
    {
        if (_runtime is null)
        {
            return;
        }

        DotNetDispatcher.EndInvokeJS(_runtime, arguments);
    }

    public static void BeginDotNetInvoke(string callId, string? assemblyName, string methodIdentifier,
        int dotNetObjectId, string argsJson)
    {
        if (_runtime is null)
        {
            return;
        }

        var info = new DotNetInvocationInfo(assemblyName, methodIdentifier, dotNetObjectId, callId);
        DotNetDispatcher.BeginInvokeDotNet(_runtime, info, argsJson);
    }

    public static byte[] PullDownload(string token)
    {
        if (_session is null || string.IsNullOrEmpty(token))
        {
            return Array.Empty<byte>();
        }

        var sink = _session.Services.GetService<IDownloadSink>() as WasmDownloadSink;
        return sink?.Pull(token) ?? Array.Empty<byte>();
    }
#endif
}
