#if RASK_BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
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

    public static void Init(WasmLiveSession session) => _session = session;

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

    // Routes InvokeJsAsync<T> results back from the JS-side scoped-JS dispatcher
    // to the C# JsInvokeResultStore. `payload` is either null, a JSON-primitive
    // value as string (the JS shim JSON-stringifies non-string results), or a
    // raw string. The store deserializes per the awaiting Task's T.
    [JSExport]
    public static void ResolveJsInvoke(int id, string? payload, string? error)
    {
        if (error is not null)
        {
            JsInvokeResultStore.TryResolve(id, null, error);
            return;
        }

        if (payload is null)
        {
            JsInvokeResultStore.TryResolve(id, null, null);
            return;
        }

        try
        {
            // payload is JSON.stringify output OR a string passed through. Try
            // parsing as JSON; on failure, treat as a raw string and re-encode
            // as a JSON string literal with a hand-rolled escape (trim-safe).
            using var doc = JsonDocument.Parse(payload);
            JsInvokeResultStore.TryResolve(id, doc.RootElement.Clone(), null);
        }
        catch (JsonException)
        {
            var quoted = JsonEncodedText.Encode(payload).ToString();
            using var doc = JsonDocument.Parse("\"" + quoted + "\"");
            JsInvokeResultStore.TryResolve(id, doc.RootElement.Clone(), null);
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

    // Non-browser stub mirroring the JSExport above so tests can drive the same code path.
    public static void ResolveJsInvoke(int id, string? payload, string? error) =>
        JsInvokeResultStore.TryResolve(id,
            payload is null ? null : JsonDocument.Parse(payload).RootElement.Clone(), error);

    public static byte[] PullDownload(string token)
    {
        if (_session is null || string.IsNullOrEmpty(token)) return Array.Empty<byte>();
        var sink = _session.Services.GetService<IDownloadSink>() as WasmDownloadSink;
        return sink?.Pull(token) ?? Array.Empty<byte>();
    }
#endif
}
