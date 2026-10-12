using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// The browser host's side of a page reached in place: it is sent before its OnUpdated is back, and again
// after. Pinned as it is today; the first test flips when a navigation waits for the page it goes to.
[Collection("WasmSession")]
public sealed class ListArrivalFramesTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    [Fact]
    public async Task Today_a_list_reached_by_a_link_is_sent_before_its_read_is_back_and_again_after()
    {
        HeldListStubApp.Hold();
        var (session, _) = NewSession<HeldListStubApp>(diffMode: DiffMode);
        await session.InitialRenderAsync();

        var before = Encoding.UTF8.GetString(await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/list","query":""}""")));
        HeldListStubApp.Release();
        await ShowsTheRows(session);
        var after = Encoding.UTF8.GetString(session.LastSentFrame);

        // Wanted: one frame, with the rows. Today the heading goes first.
        Assert.Contains("<h1>list</h1>", before, StringComparison.Ordinal);
        Assert.DoesNotContain("rows:15", before, StringComparison.Ordinal);
        Assert.StartsWith("{\"kind\":\"diff\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Today_a_first_render_in_the_browser_does_not_wait_for_the_page_either()
    {
        HeldListStubApp.Hold();
        var (session, services) = NewSession<HeldListStubApp>(diffMode: DiffMode);
        services.GetRequiredService<RouteState>().Path = "/list";

        var first = Encoding.UTF8.GetString(await session.InitialRenderAsync());
        HeldListStubApp.Release();
        await ShowsTheRows(session);

        Assert.Contains("<h1>list</h1>", first, StringComparison.Ordinal);
        Assert.DoesNotContain("rows:15", first, StringComparison.Ordinal);
    }

    private static Task ShowsTheRows(WasmLiveSession session) =>
        WaitFor.True(
            () => Encoding.UTF8.GetString(session.LastSentFrame).Contains("rows:15", StringComparison.Ordinal),
            "the list's rows in a second frame");
}
