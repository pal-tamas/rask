using System.Text;
using Microsoft.JSInterop;

namespace Rask.Wasm.Tests.JsInteropRuntime;

// Exercises the WASM IJSRuntime round-trip via the non-browser test seam in
// Rask.Wasm.JSInterop: BeginInvokeJS calls are recorded as LastBeginInvokeJsCall,
// and EndInvokeJSResult routes a synthesized [taskId, success, result|error]
// triple back through DotNetDispatcher.EndInvokeJS — the same code path the
// real JSExport calls in a browser. Tests in this class share the JSInterop
// static singleton (_runtime), so the class runs sequentially under xunit's
// default per-class collection.
public sealed class WasmJSRuntimeTests
{
    [Fact]
    public async Task InvokeAsync_RoundTrip_CompletesWithJsResult()
    {
        var runtime = new WasmJSRuntime();
        Rask.Wasm.JSInterop.Init(runtime);

        var task = runtime.InvokeAsync<string>("sessionStorage.getItem", "my-key").AsTask();

        var call = Rask.Wasm.JSInterop.LastBeginInvokeJsCall;
        Assert.NotNull(call);
        Assert.Equal("sessionStorage.getItem", call!.Identifier);
        Assert.Equal("[\"my-key\"]", call.ArgsJson);

        var taskId = long.Parse(call.TaskId);
        Rask.Wasm.JSInterop.EndInvokeJSResult(BuildResult(taskId, success: true, resultJson: "\"stored-value\""));

        var observed = await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("stored-value", observed);
    }

    [Fact]
    public async Task InvokeAsync_ErrorReply_PropagatesAsJSException()
    {
        var runtime = new WasmJSRuntime();
        Rask.Wasm.JSInterop.Init(runtime);

        var task = runtime.InvokeAsync<string>("nonexistent.method").AsTask();

        var taskId = long.Parse(Rask.Wasm.JSInterop.LastBeginInvokeJsCall!.TaskId);
        Rask.Wasm.JSInterop.EndInvokeJSResult(BuildResult(taskId, success: false, error: "TypeError: boom"));

        var ex = await Assert.ThrowsAsync<JSException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("TypeError: boom", ex.Message);
    }

    [Fact]
    public async Task InvokeVoidAsync_VoidResult_Completes()
    {
        var runtime = new WasmJSRuntime();
        Rask.Wasm.JSInterop.Init(runtime);

        var task = runtime.InvokeVoidAsync("sessionStorage.setItem", "k", "v").AsTask();

        var call = Rask.Wasm.JSInterop.LastBeginInvokeJsCall;
        Assert.NotNull(call);
        // JSCallResultType.JSVoidResult == 3 — verifies the void overload routes through
        // the same dispatch surface without expecting a serializable result.
        Assert.Equal(3, call!.ResultType);

        var taskId = long.Parse(call.TaskId);
        Rask.Wasm.JSInterop.EndInvokeJSResult(BuildResult(taskId, success: true, resultJson: "null"));

        await task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    // Mirrors the [taskId, success, result|error] shape DotNetDispatcher.EndInvokeJS parses —
    // same format RaskEndpointExtensions.HandleJsResult (Server) and the JS-side
    // endInvokeJSResult (rask.wasm.js) build.
    private static string BuildResult(long taskId, bool success, string? resultJson = null, string? error = null)
    {
        var sb = new StringBuilder(64);
        sb.Append('[').Append(taskId).Append(',').Append(success ? "true" : "false").Append(',');
        if (success)
        {
            sb.Append(resultJson ?? "null");
        }
        else
        {
            sb.Append('"').Append((error ?? "failure").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
        }

        sb.Append(']');
        return sb.ToString();
    }
}
