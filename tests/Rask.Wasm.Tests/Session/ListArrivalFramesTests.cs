using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// The browser host's side of a page reached in place: the navigation shows the page it goes to at once, with its
// address, and what the page reads follows in a frame of its own. The read is held and released by the test.
[Collection("WasmSession")]
public sealed class ListArrivalFramesTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    private const string ToTheList = """{"type":"navigate","path":"/list","query":""}""";

    [Fact]
    public async Task A_list_reached_by_a_link_is_shown_at_once_with_its_address_and_its_rows_follow()
    {
        HeldListStubApp.Hold();
        var (session, _) = NewSession<HeldListStubApp>(diffMode: DiffMode);
        await session.InitialRenderAsync();

        var atOnce = Encoding.UTF8.GetString(await session.DispatchAsync(Utf8(ToTheList)));
        HeldListStubApp.Release();
        await ShowsTheRows(session);
        var after = Encoding.UTF8.GetString(session.LastSentFrame);

        Assert.Contains("<h1>list</h1>", atOnce, StringComparison.Ordinal);
        Assert.DoesNotContain("rows:15", atOnce, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"/list\"", atOnce, StringComparison.Ordinal);
        Assert.StartsWith("{\"kind\":\"diff\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_render_in_the_browser_shows_the_page_it_opens_on_at_once_and_its_rows_follow()
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

    [Fact]
    public async Task A_page_with_nothing_to_wait_for_is_shown_with_its_address()
    {
        var (session, _) = NewSession<HeldListStubApp>(diffMode: DiffMode);
        await session.InitialRenderAsync();

        var home = Encoding.UTF8.GetString(
            await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/","query":""}""")));

        Assert.Contains("\"url\":\"/\"", home, StringComparison.Ordinal);
    }

    private static Task ShowsTheRows(WasmLiveSession session) =>
        WaitFor.True(
            () => Encoding.UTF8.GetString(session.LastSentFrame).Contains("rows:15", StringComparison.Ordinal),
            "the list's rows in a second frame");
}
