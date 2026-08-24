using System.Text.Json;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Native.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Native.Tests.Session;

// Diagnostic: does a handler that AWAITS an IJSRuntime result complete over the native message bridge?
// Uses FakeNativeWebView (no Playwright) to isolate native-core from the E2E shim: dispatch a click whose
// handler awaits js.InvokeAsync, confirm DispatchOutsideRender evaluated beginInvokeJS, then post the
// jsResult the real client would post and assert the handler resumed and rendered the result.
[Collection("NativeSession")]
public class NativeJsInteropTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task Handler_awaiting_js_result_resumes_when_jsResult_posts()
    {
        var (_, webView, initial) = await NewSessionAsync<NativeJsInteropApp>(diffMode: DiffMode);
        var handlerId = MarkupAssert.FirstHandlerId(initial);

        // The handler awaits js.InvokeAsync<string>, so this dispatch won't complete until we post the
        // jsResult (the real client runs beginInvokeJS and posts the result back).
        var dispatch = webView.PostAsync($$"""{"id":"{{handlerId}}","type":"click"}""");

        var eval = await WaitForEvalContainingAsync(webView, "beginInvokeJS");

        // Regression guard (this is what broke IJSRuntime-with-args on the real WebView): argsJson must be
        // embedded as a JS STRING literal so the client's JSON.parse(argsJson) works — NOT as a raw array
        // literal, which would arrive already-parsed and make JSON.parse choke on the coerced text.
        Assert.Contains("\"sessionStorage.getItem\",\"", eval);
        Assert.DoesNotContain("\"sessionStorage.getItem\",[", eval);

        var taskId = eval[(eval.IndexOf('(') + 1)..eval.IndexOf(',')].Trim();

        await webView.PostAsync($$"""{"type":"jsResult","id":{{taskId}},"success":true,"result":"hello-native"}""");

        await dispatch.WaitAsync(TimeSpan.FromSeconds(5));

        using var doc = JsonDocument.Parse(webView.LastFrame.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("result=hello-native", html);
    }

    private static async Task<string> WaitForEvalContainingAsync(FakeNativeWebView webView, string needle)
    {
        for (var i = 0; i < 200; i++)
        {
            var hit = webView.Evaluated.FirstOrDefault(e => e.Contains(needle, StringComparison.Ordinal));
            if (hit is not null)
            {
                return hit;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"No evaluated JS containing '{needle}' after 2s. " +
            $"Evaluated: [{string.Join(" | ", webView.Evaluated)}]");
    }
}

internal sealed partial class NativeJsInteropApp : Component
{
    private readonly IJSRuntime _js;
    public string? Result;

    public NativeJsInteropApp(IJSRuntime js) => _js = js;

    protected override Component? HeadAssets => Title["js"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P[$"result={Result ?? "(none)"}"],
        Button
            .OnClickAsync(async () =>
        {
            Result = await _js.InvokeAsync<string>("sessionStorage.getItem", "k");
        })["go"]
    ];
}
