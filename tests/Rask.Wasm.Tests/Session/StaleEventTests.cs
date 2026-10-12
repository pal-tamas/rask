using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

/// <summary>
///     An event that names an older page than the one the session holds reaches the handler it was sent to or
///     nothing — in the browser host too, where the race is an event queued behind the call that changed the page.
/// </summary>
[Collection("WasmSession")]
public sealed class StaleEventTests : ResettingTestBase, IDisposable
{
    private readonly List<string> _frames = [];

    public StaleEventTests()
        : base(LiveDiffMode.DisabledFull) =>
        JSInterop.AppliedFrames = _frames;

    public void Dispose() => JSInterop.AppliedFrames = null;

    [Fact]
    public async Task A_click_on_the_last_row_sent_before_a_row_was_removed_asks_about_that_row()
    {
        var (session, asking) = await StartAskingAsync();
        await session.DispatchAsync(Utf8(Click(asking, "yes")));

        await session.DispatchAsync(Utf8(Click(asking, "bin3")));

        Assert.Contains("rows=2,3;removing=3;removed=1;closed=0;", HtmlOf(_frames[^1]));
    }

    [Fact]
    public async Task A_second_click_on_the_bin_of_a_removed_row_runs_nothing_and_sends_nothing()
    {
        var (session, asking) = await StartAskingAsync();
        await session.DispatchAsync(Utf8(Click(asking, "yes")));
        var sent = _frames.Count;

        var answer = await session.DispatchAsync(Utf8(Click(asking, "bin1")));

        Assert.Empty(answer);
        Assert.Equal(sent, _frames.Count);
        Assert.Contains("rows=2,3;removing=;removed=1;closed=0;", HtmlOf(_frames[^1]));
    }

    [Fact]
    public async Task A_click_in_the_batch_that_removed_a_row_before_it_reaches_its_own_row()
    {
        var (session, asking) = await StartAskingAsync();

        await session.DispatchAsync(Batch(Click(asking, "yes"), Click(asking, "bin3")));

        Assert.Contains("rows=2,3;removing=3;removed=1;closed=0;", HtmlOf(_frames[^1]));
    }

    [Fact]
    public async Task A_double_click_in_one_batch_on_the_bin_of_a_row_being_removed_removes_one_row()
    {
        var (session, asking) = await StartAskingAsync();

        await session.DispatchAsync(Batch(Click(asking, "yes"), Click(asking, "bin1"), Click(asking, "yes")));

        Assert.Contains("rows=2,3;removing=;removed=1;closed=0;", HtmlOf(_frames[^1]));
    }

    [Fact]
    public async Task The_first_page_a_session_paints_is_page_zero_and_each_page_whose_handlers_moved_is_the_next()
    {
        var (session, _) = NewSession<LedgerStubApp>(diffMode: DiffMode);
        var first = Encoding.UTF8.GetString(await session.InitialRenderAsync());

        await session.DispatchAsync(Utf8(Click(first, "bin1")));

        Assert.Equal(0, VersionOf(first));
        Assert.Equal(1, VersionOf(_frames[^1]));
    }

    // The session with the first row's bin pressed: the page the browser holds asks whether to remove row 1.
    private async Task<(WasmLiveSession Session, string Asking)> StartAskingAsync()
    {
        var (session, _) = NewSession<LedgerStubApp>(diffMode: DiffMode);
        var first = Encoding.UTF8.GetString(await session.InitialRenderAsync());
        await session.DispatchAsync(Utf8(Click(first, "bin1")));
        return (session, _frames[^1]);
    }

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static byte[] Batch(params string[] events) =>
        Utf8($$"""{"type":"batch","events":[{{string.Join(',', events)}}]}""");

    // A click on a button of `payload`'s page, naming that page as the browser does.
    private static string Click(string payload, string button)
    {
        var match = Regex.Match(HtmlOf(payload), $"id=\"{button}\"[^>]*data-rask-on-click=\"([^\"]+)\"");
        Assert.True(match.Success, $"button '{button}' not found");
        return $$"""{"id":"{{match.Groups[1].Value}}","type":"click","v":{{VersionOf(payload)}}}""";
    }

    private static string HtmlOf(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static int VersionOf(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("v").GetInt32();
    }
}
