#if RASK_BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop.Infrastructure;
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

    public static void Init(WasmLiveSession session) => _session = session;

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

        // Push the result back through the existing applyRender JSImport instead of
        // returning Task<byte[]> (unsupported by the JSExport source generator). One
        // boundary crossing each way, both byte[] — total interop cost is the same as
        // the prior Task<string> pull model but without the per-event JSON.stringify in
        // JS + UTF-16 transcode in the marshalling layer.
        var payload = await _session.DispatchAsync(json).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            ApplyRender(payload);
        }
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
            Console.Error.WriteLine($"[Rask.Wasm] EndInvokeJSResult dispatch failed: {ex}");
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
            Console.Error.WriteLine(
                $"[Rask.Wasm] BeginDotNetInvoke '{assemblyName}.{methodIdentifier}' failed: {ex}");
        }
    }

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

    [JSImport("applyRender", ModuleName)]
    public static partial void ApplyRender(byte[] payload);

    [JSImport("getLocation", ModuleName)]
    public static partial string GetLocation();

    [JSImport("getBaseAddress", ModuleName)]
    public static partial string GetBaseAddress();

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

    public static void ApplyRender(byte[] payload) { }
    public static string GetLocation() => "/";
    public static string GetBaseAddress() => "/";
    public static void PushHistory(string url, bool replace) { }

    public static Task<byte[]> ReadFileChunkAsync(string @ref, int offset, int length) =>
        Task.FromResult(Array.Empty<byte>());

    // Records the last BeginInvokeJSImport call so tests can assert dispatch shape.
    public record BeginInvokeJsCall(string TaskId, string Identifier, string? ArgsJson, int ResultType, string TargetInstanceId);
    public static BeginInvokeJsCall? LastBeginInvokeJsCall { get; private set; }

    public static void BeginInvokeJSImport(string taskId, string identifier, string? argsJson, int resultType, string targetInstanceId) =>
        LastBeginInvokeJsCall = new BeginInvokeJsCall(taskId, identifier, argsJson, resultType, targetInstanceId);

    public static string? LastEndDotNetInvoke { get; private set; }
    public static void EndDotNetInvokeImport(string resultJson) => LastEndDotNetInvoke = resultJson;

    public static void EndInvokeJSResult(string arguments)
    {
        if (_runtime is null) return;
        DotNetDispatcher.EndInvokeJS(_runtime, arguments);
    }

    public static void BeginDotNetInvoke(string callId, string? assemblyName, string methodIdentifier, int dotNetObjectId, string argsJson)
    {
        if (_runtime is null) return;
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
