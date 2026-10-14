using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// The browser host's side of a page that calls Go() as it mounts or updates: the frame the page is sent is its
// destination's, with the destination's address and title, and nothing of the page that sent the reader on.
//
// A page that loads first is sent on by a frame of its own, pushed when the load ends. It is found among the
// frames the session pushed rather than read as "the last one": the load's end also asks for a render, and on
// more than one thread that render may be pushed after the destination's frame instead of before it.
[Collection("WasmSession")]
public sealed class RedirectWhileMountingTests : ResettingTestBase, IDisposable
{
    private readonly List<string> _frames = [];

    public RedirectWhileMountingTests()
        : base(LiveDiffMode.DisabledFull) =>
        JSInterop.AppliedFrames = _frames;

    public void Dispose() => JSInterop.AppliedFrames = null;

    [Fact]
    public async Task A_link_to_a_page_that_redirects_as_it_mounts_lands_on_its_destination()
    {
        var session = await OpenAt("/rs/start");

        var frame = await Navigate(session, "/rs/moved");

        Assert.Equal(("/rs/home", "push"), History(frame));
        Assert.Contains("home-content", Html(frame), StringComparison.Ordinal);
        Assert.Contains(">Home</title>", Html(frame), StringComparison.Ordinal);
        Assert.Contains("crumb:Home", Html(frame), StringComparison.Ordinal);
        Assert.DoesNotContain("moved-content", Html(frame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_load_of_a_page_that_redirects_shows_its_destination_and_replaces_the_address()
    {
        var (session, services) = NewSession<RedirectStubApp>(diffMode: DiffMode);
        services.GetRequiredService<RouteState>().Path = "/rs/moved";

        var frame = await session.InitialRenderAsync();

        Assert.Equal(("/rs/home", "replace"), History(frame));
        Assert.Contains("home-content", Html(frame), StringComparison.Ordinal);
        Assert.DoesNotContain("moved-content", Html(frame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chain_of_pages_that_each_redirect_lands_on_the_last_destination()
    {
        var session = await OpenAt("/rs/start");

        var frame = await Navigate(session, "/rs/first");

        Assert.Equal(("/rs/home", "push"), History(frame));
        Assert.Contains("home-content", Html(frame), StringComparison.Ordinal);
        Assert.DoesNotContain("first-content", Html(frame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_pages_that_redirect_to_each_other_end_on_the_error_page_instead_of_hanging()
    {
        var session = await OpenAt("/rs/start");

        var looped = await Navigate(session, "/rs/ping");

        Assert.Matches("^/rs/p[io]ng$", History(looped).Url);
        Assert.Contains("Application error", Html(looped), StringComparison.Ordinal);
        Assert.DoesNotContain("start-content", Html(looped), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_redirects_when_its_address_changes_lands_on_its_destination()
    {
        var session = await OpenAt("/rs/item/1");

        var second = await Navigate(session, "/rs/item/2");
        var missing = await Navigate(session, "/rs/item/0");

        Assert.Contains("item-2", Html(second), StringComparison.Ordinal);
        Assert.Equal(("/rs/home", "push"), History(missing));
        Assert.Contains("home-content", Html(missing), StringComparison.Ordinal);
        Assert.DoesNotContain("item-0", Html(missing), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handler_that_goes_to_a_page_that_redirects_lands_on_its_destination()
    {
        var (session, services) = NewSession<RedirectStubApp>(diffMode: DiffMode);
        services.GetRequiredService<RouteState>().Path = "/rs/start";
        var handler = Regex.Match(
            Html(await session.InitialRenderAsync()), "id=\"to-moved\"[^>]*data-rask-on-click=\"([^\"]+)\"",
            RegexOptions.None, TimeSpan.FromSeconds(1));

        var frame = await session.DispatchAsync(Utf8($$"""{"id":"{{handler.Groups[1].Value}}","type":"click"}"""));

        Assert.Equal(("/rs/home", "push"), History(frame));
        Assert.Contains("home-content", Html(frame), StringComparison.Ordinal);
        Assert.DoesNotContain("moved-content", Html(frame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_to_a_page_that_loads_and_then_redirects_lands_on_its_destination_replacing_its_address()
    {
        var session = await OpenAt("/rs/start");

        var loading = await Navigate(session, "/rs/late");
        var landed = await Landed();

        Assert.Equal(("/rs/late", "push"), History(loading));
        Assert.Equal(("/rs/home", "replace"), History(landed));
        Assert.Contains(">Home</title>", Html(landed), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handler_that_goes_to_a_page_that_loads_and_then_redirects_lands_on_its_destination()
    {
        var (session, services) = NewSession<RedirectStubApp>(diffMode: DiffMode);
        services.GetRequiredService<RouteState>().Path = "/rs/start";
        var handler = Regex.Match(
            Html(await session.InitialRenderAsync()), "id=\"to-late\"[^>]*data-rask-on-click=\"([^\"]+)\"",
            RegexOptions.None, TimeSpan.FromSeconds(1));

        await session.DispatchAsync(Utf8($$"""{"id":"{{handler.Groups[1].Value}}","type":"click"}"""));
        var landed = await Landed();

        Assert.Equal(("/rs/home", "replace"), History(landed));
    }

    [Fact]
    public async Task A_first_load_of_a_page_that_loads_and_then_redirects_shows_its_destination_once_loaded()
    {
        var (session, services) = NewSession<RedirectStubApp>(diffMode: DiffMode);
        services.GetRequiredService<RouteState>().Path = "/rs/late";

        var first = await session.InitialRenderAsync();
        var landed = await Landed();

        Assert.Contains("late-content", Html(first), StringComparison.Ordinal);
        Assert.Equal(("/rs/home", "replace"), History(landed));
    }

    // The handler's await ends beside the repaint its page gets for the wait. Whichever comes first, the
    // destination is not mounted by that repaint: only the navigation's own render mounts it.
    [Fact]
    public async Task A_handler_that_navigates_while_its_page_is_being_repainted_still_lands_on_the_destination()
    {
        var (session, services) = NewSession<RedirectStubApp>(diffMode: DiffMode);
        services.GetRequiredService<RouteState>().Path = "/rs/start";
        var handler = Regex.Match(
            Html(await session.InitialRenderAsync()), "id=\"late-under-repaint\"[^>]*data-rask-on-click=\"([^\"]+)\"",
            RegexOptions.None, TimeSpan.FromSeconds(1));

        await session.DispatchAsync(Utf8($$"""{"id":"{{handler.Groups[1].Value}}","type":"click"}"""));
        var landed = await Landed();

        Assert.Equal(("/rs/home", "replace"), History(landed));
        Assert.Contains("home-content", Html(landed), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_the_reader_has_left_by_the_time_its_load_ends_sends_them_nowhere()
    {
        RedirectStubGated.Opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RedirectStubGated.Asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (session, services) = NewSession<RedirectStubApp>(diffMode: DiffMode);
        var route = services.GetRequiredService<RouteState>();
        route.Path = "/rs/start";
        await session.InitialRenderAsync();
        await Navigate(session, "/rs/gated");
        var back = await Navigate(session, "/rs/start");

        RedirectStubGated.Opened.SetResult();
        await RedirectStubGated.Asked.Task;
        var settled = await Navigate(session, "/rs/start");

        Assert.Contains("start-content", Html(back), StringComparison.Ordinal);
        Assert.Equal("/rs/start", route.Path);
        Assert.Contains("start-content", Html(settled), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handler_that_goes_out_asks_the_browser_for_a_full_page_navigation_to_the_address_as_written()
    {
        var (session, services) = NewSession<RedirectStubApp>(diffMode: DiffMode);
        services.GetRequiredService<RouteState>().Path = "/rs/start";
        var handler = Regex.Match(
            Html(await session.InitialRenderAsync()), "id=\"out\"[^>]*data-rask-on-click=\"([^\"]+)\"",
            RegexOptions.None, TimeSpan.FromSeconds(1));

        await session.DispatchAsync(Utf8($$"""{"id":"{{handler.Groups[1].Value}}","type":"click"}"""));

        Assert.Equal("/tenants", JSInterop.LastLeftTo);
        Assert.Equal("/rs/start", services.GetRequiredService<RouteState>().Path);
    }

    [Fact]
    public async Task A_link_to_a_page_that_goes_out_as_it_mounts_asks_the_browser_for_a_full_page_navigation()
    {
        var session = await OpenAt("/rs/start");

        await Navigate(session, "/rs/out");

        Assert.Equal("/old/partners", JSInterop.LastLeftTo);
    }

    // The first frame pushed to the page that shows the destination and says where the reader now is.
    private async Task<byte[]> Landed()
    {
        string? landed = null;
        await WaitFor.True(() => (landed = FirstPushed(IsTheDestination)) is not null, "a frame that lands on the destination");
        return Utf8(landed!);
    }

    private static bool IsTheDestination(string frame) =>
        frame.Contains("home-content", StringComparison.Ordinal) && History(Utf8(frame)).Url is not null;

    // By index, never by enumerator: the session appends from its own thread while this reads.
    private string? FirstPushed(Func<string, bool> isIt)
    {
        for (var i = 0; i < _frames.Count; i++)
        {
            if (_frames[i] is { } frame && isIt(frame))
            {
                return frame;
            }
        }

        return null;
    }

    private async Task<WasmLiveSession> OpenAt(string path)
    {
        var (session, services) = NewSession<RedirectStubApp>(diffMode: DiffMode);
        services.GetRequiredService<RouteState>().Path = path;
        await session.InitialRenderAsync();
        return session;
    }

    private static Task<byte[]> Navigate(WasmLiveSession session, string path) =>
        session.DispatchAsync(Utf8($$"""{"type":"navigate","path":"{{path}}","query":""}"""));

    private static string Html(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static (string? Url, string? Action) History(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.TryGetProperty("history", out var history)
            ? (history.GetProperty("url").GetString(), history.GetProperty("action").GetString())
            : (null, null);
    }
}
