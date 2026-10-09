using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     A frame that names an older page than the one the server holds — written here by hand, as a browser's click
///     arrives when it left before the reply to the one before it — reaches the handler it was sent to or nothing.
/// </summary>
/// <remarks>
///     Every event carries a <c>seq</c> and the test reads until its ack, which follows the event's render: what
///     was read by then is everything the event did. The ledger tests read state off the whole page, so their host
///     has the diff codec off.
/// </remarks>
public class StaleEventTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_click_on_the_last_row_sent_before_a_row_was_removed_asks_about_that_row(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<LedgerApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, transport);
        await using var connection = ws;
        var asking = await ClickAsync(ws, Id(page, "bin1"), version: 0, seq: 1);
        await ClickAsync(ws, Id(HtmlOf(asking), "yes"), VersionOf(asking), seq: 2);

        var after = await ClickAsync(ws, Id(HtmlOf(asking), "bin3"), VersionOf(asking), seq: 3);

        Assert.Contains("rows=2,3;removing=3;removed=1;closed=0;", HtmlOf(after));
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_second_click_on_the_bin_of_a_removed_row_is_acked_and_runs_nothing(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<LedgerApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, transport);
        await using var connection = ws;
        var asking = await ClickAsync(ws, Id(page, "bin1"), version: 0, seq: 1);
        var removed = await ClickAsync(ws, Id(HtmlOf(asking), "yes"), VersionOf(asking), seq: 2);

        await ws.SendJsonAsync(Click(Id(HtmlOf(asking), "bin1"), VersionOf(asking), seq: 3));
        var (renders, acked) = await ReadUntilAckAsync(ws);

        Assert.Empty(renders);
        Assert.Equal(3, acked);
        Assert.Contains("rows=2,3;removing=;removed=1;closed=0;", HtmlOf(removed));
    }

    [Fact]
    public async Task The_same_stale_click_without_a_page_number_lands_on_whatever_holds_its_id_now()
    {
        using var host = RaskTestHost.Create<LedgerApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;
        var asking = await ClickAsync(ws, Id(page, "bin1"), version: 0, seq: 1);
        await ClickAsync(ws, Id(HtmlOf(asking), "yes"), VersionOf(asking), seq: 2);

        // What a frame that says nothing about its page has always done, and why the browser now says it.
        var after = await ClickAsync(ws, Id(HtmlOf(asking), "bin3"), version: null, seq: 3);

        Assert.Contains("removing=;removed=1;closed=1;", HtmlOf(after));
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_click_in_the_batch_that_removed_a_row_before_it_reaches_its_own_row(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<LedgerApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, transport);
        await using var connection = ws;
        var asking = await ClickAsync(ws, Id(page, "bin1"), version: 0, seq: 1);
        var v = VersionOf(asking);

        // One task's worth: the row is removed between the two, and the second still names the page before it.
        await ws.SendJsonAsync(new { type = "batch", events = new[] { Click(Id(HtmlOf(asking), "yes"), v, seq: 2), Click(Id(HtmlOf(asking), "bin3"), v, seq: 3) } });
        var (renders, _) = await ReadUntilAckAsync(ws);

        Assert.Contains("rows=2,3;removing=3;removed=1;closed=0;", HtmlOf(renders[^1]));
    }

    [Fact]
    public async Task A_double_click_in_one_batch_on_the_bin_of_a_row_being_removed_removes_one_row()
    {
        using var host = RaskTestHost.Create<LedgerApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;
        var asking = await ClickAsync(ws, Id(page, "bin1"), version: 0, seq: 1);
        var v = VersionOf(asking);
        var html = HtmlOf(asking);

        await ws.SendJsonAsync(new { type = "batch", events = new[] { Click(Id(html, "yes"), v, seq: 2), Click(Id(html, "bin1"), v, seq: 3), Click(Id(html, "yes"), v, seq: 4) } });
        var (renders, acked) = await ReadUntilAckAsync(ws);

        Assert.Contains("rows=2,3;removing=;removed=1;closed=0;", HtmlOf(renders[^1]));
        Assert.Equal(4, acked);
    }

    [Fact]
    public async Task A_diff_of_a_page_whose_handlers_stayed_carries_no_page_number()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        // The page's one handler is a lambda over the page itself: the same handler in every render.
        var bumped = await ClickAsync(ws, MarkupAssert.FirstHandlerId(page), version: 0, seq: 1);

        Assert.Contains("\"kind\":\"diff\"", bumped);
        Assert.Null(VersionOf(bumped));
    }

    [Fact]
    public async Task A_diff_of_a_page_whose_handlers_moved_carries_the_next_page_number()
    {
        using var host = RaskTestHost.Create<EventBatchApp>();
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        // `snap` closes over what its render computed: a different handler after every render of the page.
        var bumped = await ClickAsync(ws, Id(page, "bump"), version: 0, seq: 1);
        var again = await ClickAsync(ws, Id(page, "bump"), version: 1, seq: 2);

        Assert.Contains("\"kind\":\"diff\"", bumped);
        Assert.Equal((1, 2), (VersionOf(bumped), VersionOf(again)));
    }

    [Fact]
    public async Task A_whole_page_always_carries_its_page_number()
    {
        using var host = RaskTestHost.Create<LedgerApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        var asking = await ClickAsync(ws, Id(page, "bin1"), version: 0, seq: 1);
        var closed = await ClickAsync(ws, Id(HtmlOf(asking), "no"), VersionOf(asking), seq: 2);

        Assert.Equal(1, VersionOf(asking));
        Assert.Equal(2, VersionOf(closed));
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_page_rebuilt_for_a_browser_that_reconnects_says_which_page_it_is(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<LedgerApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, first) = await OpenAsync(host, LiveTransportKind.WebSocket);
        var asking = await ClickAsync(first, Id(page, "bin1"), version: 0, seq: 1);
        var token = ResumeOf(asking) ?? ResumeOf(await ClickAsync(first, Id(HtmlOf(asking), "no"), VersionOf(asking), seq: 2));
        await first.DisposeAsync();
        var sessionId = MarkupAssert.SessionId(page);
        await host.Store.RemoveAsync(sessionId);

        // The browser still holds page 1 of a session no server knows. What it is sent is a page of a new one.
        await using var ws = await LiveTestConnection.OpenAsync(host, transport, sessionId, token);
        var rebuilt = await ws.ReceiveUntilAsync(frame => frame.Contains("\"html\"", StringComparison.Ordinal), "the rebuilt page");
        var after = await ClickAsync(ws, Id(HtmlOf(rebuilt), "bin2"), VersionOf(rebuilt), seq: 9);

        Assert.NotNull(VersionOf(rebuilt));
        Assert.Contains("removing=2;", HtmlOf(after));
    }

    private static async Task<(string Page, ILiveTestConnection Ws)> OpenAsync(RaskTestHost host, LiveTransportKind transport)
    {
        var page = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var ws = await LiveTestConnection.OpenAsync(host, transport, MarkupAssert.SessionId(page));
        return (page, ws);
    }

    private static Dictionary<string, object> Click(string id, int? version, int seq)
    {
        var click = new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = id, ["type"] = "click", ["seq"] = seq };
        if (version is { } v)
        {
            click["v"] = v;
        }

        return click;
    }

    // Sends one click and answers the render it caused.
    private static async Task<string> ClickAsync(ILiveTestConnection ws, string id, int? version, int seq)
    {
        await ws.SendJsonAsync(Click(id, version, seq));
        var (renders, _) = await ReadUntilAckAsync(ws);
        return Assert.Single(renders);
    }

    private static string Id(string page, string button)
    {
        var match = Regex.Match(page, $"id=\"{button}\"[^>]*data-rask-on-click=\"([^\"]+)\"");
        Assert.True(match.Success, $"button '{button}' not found");
        return match.Groups[1].Value;
    }

    private static async Task<(List<string> Renders, long Acked)> ReadUntilAckAsync(ILiveTestConnection ws)
    {
        var renders = new List<string>();
        while (true)
        {
            var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(20));
            Assert.NotNull(text);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("type", out var type) && type.ValueEquals("ack"))
            {
                return (renders, doc.RootElement.GetProperty("seq").GetInt64());
            }

            renders.Add(text);
        }
    }

    private static string HtmlOf(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static int? VersionOf(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.TryGetProperty("v", out var v) ? v.GetInt32() : null;
    }

    private static string? ResumeOf(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.TryGetProperty("resume", out var resume) ? resume.GetString() : null;
    }
}
