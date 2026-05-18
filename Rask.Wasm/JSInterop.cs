#if RASK_BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif

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
    public static Task<string> Dispatch(string json) =>
        _session is null ? Task.FromResult(string.Empty) : _session.DispatchAsync(json);

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

    public static Task<string> Dispatch(string json) =>
        _session is null ? Task.FromResult(string.Empty) : _session.DispatchAsync(json);

    public static void ApplyRender(byte[] payload) { }
    public static string GetLocation() => "/";
    public static string GetBaseAddress() => "/";
    public static void PushHistory(string url, bool replace) { }
    public static Task<byte[]> ReadFileChunkAsync(string @ref, int offset, int length) =>
        Task.FromResult(Array.Empty<byte>());
#endif
}
