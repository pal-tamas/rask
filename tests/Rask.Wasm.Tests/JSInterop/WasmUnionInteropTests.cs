using System.Globalization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Tests.JsInteropRuntime;

/// <summary>
/// A C# union crosses the WebAssembly host's interop boundary as its case value, the shape System.Text.Json writes, and
/// a union of records the browser answers with reads back as the case its properties name — with nothing on the union.
/// </summary>
// Shares JSInterop's process-wide statics (the bound runtime, the last call), as WasmJSRuntimeTests does.
[Collection("WasmSession")]
public sealed class WasmUnionInteropTests
{
    public sealed record Search(string Term);

    public sealed record Pick(int Id);

    public union Selection(Search, Pick);

    [Fact]
    public async Task A_union_goes_out_as_its_case_value_and_comes_back_as_the_case_the_browser_answered()
    {
        var runtime = new WasmJSRuntime();
        JSInterop.Init(runtime);

        var task = runtime.InvokeAsync<Selection>("selection.swap", (Selection)new Search("boots")).AsTask();
        var call = JSInterop.LastBeginInvokeJsCall!;
        JSInterop.EndInvokeJSResult($$"""[{{long.Parse(call.TaskId, CultureInfo.InvariantCulture)}},true,{"id":7}]""");
        var answered = await task.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Equal("""[{"term":"boots"}]""", call.ArgsJson);
        Assert.Equal(new Pick(7), answered.Value);
    }
}
