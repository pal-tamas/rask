using System.Text.Json;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.JSInterop;

/// <summary>
/// A C# union crosses the server's interop boundary as its case value, the shape System.Text.Json writes, and a union
/// of records the browser answers with reads back as the case its properties name — with nothing on the union.
/// </summary>
public sealed class UnionInteropTests
{
    public sealed record Search(string Term);

    public sealed record Pick(int Id);

    public union Selection(Search, Pick);

    [Fact]
    public async Task A_union_goes_out_as_its_case_value_and_comes_back_as_the_case_the_browser_answered()
    {
        using var host = RaskTestHost.Create<UnionSwapApp>();
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = MarkupAssert.SessionId(html) }, ct: TestContext.Current.CancellationToken);

        using var frame = JsonDocument.Parse(await ws.ReceiveTextAsync());
        var invoke = frame.RootElement.GetProperty("jsInvokes")[0];
        await ws.SendJsonAsync(
            new { type = "jsResult", id = invoke.GetProperty("id").GetInt64(), success = true, result = new { id = 7 } },
            ct: TestContext.Current.CancellationToken);
        var answered = await UnionSwapApp.Answered.Task.WaitAsync(LiveFrames.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Equal("""[{"term":"boots"}]""", invoke.GetProperty("argsJson").GetString());
        Assert.Equal(new Pick(7), answered.Value);
    }

#pragma warning disable RASK014 // private test-helper Component subclass
    private sealed partial class UnionSwapApp(IJSRuntime js) : Component
    {
        public static TaskCompletionSource<Selection> Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Component? HeadAssets => Markup.Title["t"];

        protected override string? HtmlLang => null;

        protected override Component? Render() => Text.Value("ready");

        protected override async Task OnFirstRender()
        {
            try
            {
                Answered.TrySetResult(await js.InvokeAsync<Selection>("selection.swap", (Selection)new Search("boots")));
            }
            catch (Exception ex)
            {
                Answered.TrySetException(ex);
            }
        }
    }
#pragma warning restore RASK014
}
