#if RASK_BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif

namespace Rask.Wasm;

internal static class JSInterop
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
    public static partial void ApplyRender(string html, string? cssHash, string? cssText, string? historyJson);

    [JSImport("getLocation", ModuleName)]
    public static partial string GetLocation();

    [JSImport("getBaseAddress", ModuleName)]
    public static partial string GetBaseAddress();

    [JSImport("pushHistory", ModuleName)]
    public static partial void PushHistory(string url, bool replace);
#else
    // Non-browser stubs. Used by the test project so the pure-logic code paths can be exercised
    // without a JS runtime. None of the non-browser stubs perform real interop.
    public static Task ImportJsModuleAsync() => Task.CompletedTask;

    public static Task<string> Dispatch(string json) =>
        _session is null ? Task.FromResult(string.Empty) : _session.DispatchAsync(json);

    public static void ApplyRender(string html, string? cssHash, string? cssText, string? historyJson) { }
    public static string GetLocation() => "/";
    public static string GetBaseAddress() => "/";
    public static void PushHistory(string url, bool replace) { }
#endif
}
