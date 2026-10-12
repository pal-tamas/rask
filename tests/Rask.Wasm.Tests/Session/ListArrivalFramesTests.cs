using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// The browser host's side of a page reached in place: the navigation waits for the page it goes to, within
// the navigation budget, and shows it once. The read is held and released by the test, and the budget is either
// longer than a test may run or over while the read is still held.
[Collection("WasmSession")]
public sealed class ListArrivalFramesTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    private const string ToTheList = """{"type":"navigate","path":"/list","query":""}""";

    private static readonly TimeSpan Patient = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(40);

    [Fact]
    public async Task A_list_reached_by_a_link_is_shown_once_with_its_rows_and_its_address()
    {
        HeldListStubApp.Hold();
        var (session, _) = NewSession<HeldListStubApp>(diffMode: DiffMode, navigationBudget: Patient);
        await session.InitialRenderAsync();

        var navigating = session.DispatchAsync(Utf8(ToTheList));
        await HeldListStubApp.ReadStarted();
        var shownWhileReading = navigating.IsCompleted;
        HeldListStubApp.Release();
        var frame = Encoding.UTF8.GetString(await navigating);

        Assert.False(shownWhileReading);
        Assert.Contains("<h1>list</h1>", frame, StringComparison.Ordinal);
        Assert.Contains("rows:15", frame, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"/list\"", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_render_in_the_browser_waits_for_the_page_it_opens_on()
    {
        HeldListStubApp.Hold();
        var (session, services) = NewSession<HeldListStubApp>(diffMode: DiffMode, navigationBudget: Patient);
        services.GetRequiredService<RouteState>().Path = "/list";

        var rendering = session.InitialRenderAsync();
        await HeldListStubApp.ReadStarted();
        HeldListStubApp.Release();
        var first = Encoding.UTF8.GetString(await rendering);

        Assert.Contains("rows:15", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_list_whose_read_outlasts_the_budget_is_shown_without_rows_at_the_budget_and_the_rows_follow()
    {
        HeldListStubApp.Hold();
        var (session, _) = NewSession<HeldListStubApp>(diffMode: DiffMode, navigationBudget: Brief);
        await session.InitialRenderAsync();

        var atTheBudget = Encoding.UTF8.GetString(await session.DispatchAsync(Utf8(ToTheList)));
        HeldListStubApp.Release();
        await ShowsTheRows(session);
        var after = Encoding.UTF8.GetString(session.LastSentFrame);

        Assert.Contains("<h1>list</h1>", atTheBudget, StringComparison.Ordinal);
        Assert.DoesNotContain("rows:15", atTheBudget, StringComparison.Ordinal);
        Assert.StartsWith("{\"kind\":\"diff\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_link_pressed_while_the_first_page_is_awaited_is_the_only_page_shown()
    {
        HeldListStubApp.Hold();
        var (session, _) = NewSession<HeldListStubApp>(diffMode: DiffMode, navigationBudget: Patient);
        await session.InitialRenderAsync();

        var first = session.DispatchAsync(Utf8(ToTheList));
        await HeldListStubApp.ReadStarted();
        var second = session.DispatchAsync(Utf8("""{"type":"navigate","path":"/","query":""}"""));
        var superseded = await first;
        var shown = Encoding.UTF8.GetString(await second);
        HeldListStubApp.Release();

        Assert.Empty(superseded);
        Assert.Contains("\"url\":\"/\"", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1>list</h1>", shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_with_nothing_to_wait_for_is_shown_without_waiting_however_long_the_budget()
    {
        var (session, _) = NewSession<HeldListStubApp>(diffMode: DiffMode, navigationBudget: Patient);
        await session.InitialRenderAsync();

        var home = Encoding.UTF8.GetString(
            await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/","query":""}""")));

        // The budget is longer than this test may run: a navigation that sat it out would never come back.
        Assert.Contains("\"url\":\"/\"", home, StringComparison.Ordinal);
    }

    private static Task ShowsTheRows(WasmLiveSession session) =>
        WaitFor.True(
            () => Encoding.UTF8.GetString(session.LastSentFrame).Contains("rows:15", StringComparison.Ordinal),
            "the list's rows in a second frame");
}
