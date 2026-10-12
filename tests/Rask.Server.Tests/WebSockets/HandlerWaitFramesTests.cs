using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Cqrs;
using Rask.Querying;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// What a handler's work costs, frame by frame: nothing changes on screen until the handler has answered. What it
// sets between its awaits is withheld while it runs, and the render after it waits for the reads its change
// started, both within the handler budget — so a page change is one frame, pager and rows together. Past the
// budget the page goes out as it stands, the rest follows, and the ack (which ends the browser's busy mark)
// comes after it. The reads are held and released by the test, and the budget is either longer than a test may
// run or over before the read is let go, so nothing here depends on how fast the machine is.
public sealed class HandlerWaitFramesTests(ITestOutputHelper output)
{
    private const string OldRows = "row p1-3";
    private const string NewRows = "row p2-3";

    // The placeholder's id, as a frame's JSON writes it.
    private const string Loading = "id=\\\"loading\\\"";

    // Longer than any wait here may last: a frame is sent because the work came back, never because time ran out.
    private static void Patient(RaskServerOptions o) =>
        o.HandlerQuiescenceTimeout = o.NavigationQuiescenceTimeout = TimeSpan.FromMinutes(5);

    // Over while the test still holds the read.
    private static void Brief(RaskServerOptions o) =>
        o.HandlerQuiescenceTimeout = o.NavigationQuiescenceTimeout = TimeSpan.FromMilliseconds(40);

    private static void Off(RaskServerOptions o) => o.HandlerQuiescenceTimeout = TimeSpan.Zero;

    [Fact]
    public async Task Paging_by_a_handler_whose_read_is_within_the_budget_is_one_frame_with_the_pager_and_the_rows()
    {
        await using var page = await Page.Open("/w/plain", output, Patient);
        page.Store.Hold();

        await page.Press("next");
        await page.Store.ReadStarted();
        page.Store.Release();
        var frame = Assert.Single(await page.Settled());

        Assert.Contains("pager at 2", frame, StringComparison.Ordinal);
        Assert.Contains(NewRows, frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paging_whose_read_outlasts_the_budget_sends_the_pager_over_the_old_rows_then_the_rows_then_the_ack()
    {
        await using var page = await Page.Open("/w/plain", output, Brief);
        page.Store.Hold();

        await page.Press("next", seq: 7);
        var atTheBudget = await page.NextOfAny();
        page.Store.Release();
        var rest = await page.UntilAck(7);

        Assert.Contains("pager at 2", atTheBudget, StringComparison.Ordinal);
        Assert.Contains(OldRows, atTheBudget, StringComparison.Ordinal);
        Assert.Contains(NewRows, Assert.Single(rest), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_empties_its_rows_before_a_read_within_the_budget_never_sends_them_empty()
    {
        await using var page = await Page.Open("/w/loading", output, Patient);
        page.Store.Hold();

        await page.Press("next");
        await page.Store.ReadStarted();
        page.Store.Release();
        var frame = Assert.Single(await page.Settled());

        Assert.DoesNotContain(Loading, frame, StringComparison.Ordinal);
        Assert.Contains(NewRows, frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_empties_its_rows_shows_that_at_the_budget_when_the_read_outlasts_it()
    {
        await using var page = await Page.Open("/w/loading", output, Brief);
        page.Store.Hold();

        await page.Press("next");
        var atTheBudget = await page.Next();
        page.Store.Release();
        var rows = await page.Until(NewRows);

        Assert.Contains(Loading, atTheBudget, StringComparison.Ordinal);
        Assert.DoesNotContain(Loading, rows, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_field_changed_by_a_handler_waits_for_the_child_that_reads_again_and_is_one_frame()
    {
        await using var page = await Page.Open("/w/child", output, Patient);
        page.Store.Hold();

        await page.Press("next");
        await page.Store.ReadStarted();
        page.Store.Release();
        var frame = Assert.Single(await page.Settled());

        Assert.Contains("pager at 2", frame, StringComparison.Ordinal);
        Assert.Contains(NewRows, frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_childs_read_that_outlasts_the_budget_is_followed_by_its_rows_and_only_then_by_the_ack()
    {
        await using var page = await Page.Open("/w/child", output, Brief);
        page.Store.Hold();

        await page.Press("next", seq: 3);
        var atTheBudget = await page.NextOfAny();
        page.Store.Release();
        var rest = await page.UntilAck(3);

        // The handler returned long ago: the ack waits for the rows, not for the handler.
        Assert.Contains("pager at 2", atTheBudget, StringComparison.Ordinal);
        Assert.Contains(OldRows, atTheBudget, StringComparison.Ordinal);
        Assert.Contains(NewRows, Assert.Single(rest), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_whose_key_a_handler_moved_is_one_frame_with_the_new_rows_when_its_fetch_is_within_the_budget()
    {
        await using var page = await Page.Open("/w/query", output, Patient);
        page.Store.Hold();

        await page.Press("next");
        await page.Store.ReadStarted();
        page.Store.Release();
        var frame = Assert.Single(await page.Settled());

        Assert.Contains("pager at 2", frame, StringComparison.Ordinal);
        Assert.Contains(NewRows, frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_that_keeps_its_previous_rows_never_sends_a_frame_without_rows_and_is_acked_after_the_new_ones()
    {
        await using var page = await Page.Open("/w/query?kept=true", output, Brief);
        page.Store.Hold();

        await page.Press("next", seq: 5);
        var atTheBudget = await page.NextOfAny();
        page.Store.Release();
        var rest = await page.UntilAck(5);

        Assert.Contains("pager at 2", atTheBudget, StringComparison.Ordinal);
        Assert.Contains(OldRows, atTheBudget, StringComparison.Ordinal);
        Assert.Contains(NewRows, Assert.Single(rest), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sort_that_changes_the_address_is_acked_only_after_the_rows_its_page_was_sent_without()
    {
        await using var page = await Page.Open("/w/sorted", output, Brief);
        page.Store.Hold();

        await page.Press("sort", seq: 9);
        var atTheBudget = await page.NextOfAny();
        page.Store.Release();
        var rest = await page.UntilAck(9);

        // The busy mark and the bar are the ack's to end: sent at the budget, they never showed for a slow read.
        Assert.Equal("/w/sorted?sort=name", LiveFrames.HistoryUrl(atTheBudget));
        Assert.Contains("row unsorted-3", atTheBudget, StringComparison.Ordinal);
        Assert.Contains("row name-3", Assert.Single(rest), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handler_with_nothing_to_wait_for_is_answered_without_waiting_however_long_the_budget()
    {
        await using var page = await Page.Open("/w/save", output, Patient);

        await page.Press("ping");
        var frame = await page.Next();

        // The budget here is longer than this wait may last: a dispatch that sat it out would never be read.
        Assert.Contains("pings 1", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_that_finishes_within_the_budget_is_one_frame_and_never_says_saving()
    {
        await using var page = await Page.Open("/w/save", output, Patient);
        page.Store.Hold();

        await page.Press("save");
        await page.Store.ReadStarted();
        page.Store.Release();
        var frame = Assert.Single(await page.Settled());

        Assert.Contains("saved 1", frame, StringComparison.Ordinal);
        Assert.DoesNotContain(">saving<", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_that_outlasts_the_budget_says_saving_at_the_budget_and_saved_when_it_is()
    {
        await using var page = await Page.Open("/w/save", output, Brief);
        page.Store.Hold();

        await page.Press("save");
        var atTheBudget = await page.Next();
        page.Store.Release();
        var saved = await page.Until("saved 1");

        Assert.Contains(">saving<", atTheBudget, StringComparison.Ordinal);
        Assert.Contains("saved 0", atTheBudget, StringComparison.Ordinal);
        Assert.DoesNotContain(">saving<", saved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_async_validators_check_that_is_back_within_the_budget_sends_its_verdict_and_never_checking()
    {
        await using var page = await Page.Open("/w/checked", output, Patient);
        await page.Type("admin");
        page.Store.Hold();

        await page.Leave("admin");
        await page.Store.ReadStarted();
        page.Store.Release();
        var frame = Assert.Single(await page.Settled());

        Assert.Contains("Already taken.", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("Checking...", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_async_validators_check_that_outlasts_the_budget_says_checking_at_the_budget_and_then_its_verdict()
    {
        await using var page = await Page.Open("/w/checked", output, Brief);
        await page.Type("admin");
        page.Store.Hold();

        await page.Leave("admin");
        var atTheBudget = await page.Next();
        page.Store.Release();
        var verdict = await page.Until("Already taken.");

        Assert.Contains("Checking...", atTheBudget, StringComparison.Ordinal);
        Assert.DoesNotContain("Already taken.", atTheBudget, StringComparison.Ordinal);
        Assert.DoesNotContain("Checking...", verdict, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_budget_of_zero_sends_what_a_handler_set_before_its_await_at_once_as_it_always_did()
    {
        await using var page = await Page.Open("/w/save", output, Off);
        page.Store.Hold();

        await page.Press("save");
        var atOnce = await page.Next();
        page.Store.Release();
        var saved = await page.Until("saved 1");

        // The read is still held when this frame is read, so only a session that did not wait could have sent it.
        Assert.Contains(">saving<", atOnce, StringComparison.Ordinal);
        Assert.DoesNotContain(">saving<", saved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_budget_of_zero_acks_a_handler_at_once_and_the_rows_its_child_reads_follow_the_ack()
    {
        await using var page = await Page.Open("/w/child", output, Off);
        page.Store.Hold();

        await page.Press("next", seq: 4);
        var atOnce = await page.UntilAck(4);
        page.Store.Release();
        var rows = await page.Until(NewRows);

        Assert.Contains(OldRows, Assert.Single(atOnce), StringComparison.Ordinal);
        Assert.Contains("pager at 2", rows, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_that_never_comes_back_is_painted_at_the_budget_and_the_session_answers_the_next_event()
    {
        await using var page = await Page.Open("/w/child", output, Brief);
        page.Store.Hold();

        await page.Press("next", seq: 1);
        var first = await page.Next();
        await page.Press("next", seq: 2);
        var second = await page.Next();

        // Neither is acked before the cap, and neither holds the session's chain: nothing is left queued behind them.
        Assert.Contains("pager at 2", first, StringComparison.Ordinal);
        Assert.Contains("pager at 3", second, StringComparison.Ordinal);
        await WaitFor.True(() => page.Session.PendingHandlers == 0, "the handler chain to be empty");
    }

    [Fact]
    public async Task A_read_that_never_comes_back_is_acked_at_the_cap_and_its_rows_still_arrive_when_it_does()
    {
        await using var page = await Page.Open("/w/child", output, Brief);
        page.Session.ReadsCap = TimeSpan.FromMilliseconds(40);
        page.Store.Hold();

        await page.Press("next", seq: 1);
        var atTheBudget = await page.NextOfAny();
        var beforeTheAck = await page.UntilAck(1);
        await page.Press("next", seq: 2);
        var next = await page.Next();
        page.Store.Release();
        var rows = await page.Until("row p3-3");

        // The read is still held when the ack is read: only the cap could have sent it. Nothing was cancelled.
        Assert.Contains(OldRows, atTheBudget, StringComparison.Ordinal);
        Assert.Empty(beforeTheAck);
        Assert.Contains("pager at 3", next, StringComparison.Ordinal);
        Assert.Contains("pager at 3", rows, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cap_on_a_page_waiting_for_its_reads_is_two_seconds()
    {
        var cap = Rask.Core.Live.LiveSessionBase.DefaultReadsCap;

        Assert.Equal(TimeSpan.FromSeconds(2), cap);
    }

    [Fact]
    public async Task A_handler_that_never_returns_is_painted_at_the_budget_as_it_stands()
    {
        await using var page = await Page.Open("/w/save", output, Brief);
        page.Store.Hold();

        await page.Press("save");
        var atTheBudget = await page.Next();

        Assert.Contains(">saving<", atTheBudget, StringComparison.Ordinal);
    }

    // Every frame is a whole page here, so the page a frame shows — and the handlers on it — can be read from it.
    private sealed class Page(RaskTestHost host, WebSocket ws, ITestOutputHelper output, string html, string sessionId)
        : IAsyncDisposable
    {
        private string _html = html;

        public HandlerWaitStore Store => host.Server.Services.GetRequiredService<HandlerWaitStore>();

        public LiveSession Session => host.Store.Peek(sessionId)!;

        public static async Task<Page> Open(string path, ITestOutputHelper output, Action<RaskServerOptions> configure)
        {
            var host = RaskTestHost.Create<HandlerWaitApp>(
                configureServices: services =>
                {
                    services.AddRaskCqrs();
                    services.AddRaskQuery();
                    services.AddSingleton<HandlerWaitStore>();
                },
                configureServer: configure,
                diffMode: LiveDiffMode.DisabledFull);
            var html = await host.Http.GetStringAsync(path);
            var sessionId = MarkupAssert.SessionId(html);
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            await ws.AttachedAsync(host, sessionId);
            return new Page(host, ws, output, html, sessionId);
        }

        public Task Press(string buttonId, int? seq = null)
        {
            var handler = Regex.Match(
                _html, $"id=\"{Regex.Escape(buttonId)}\"[^>]*data-rask-on-click=\"([^\"]+)\"",
                RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
            return seq is { } stamped
                ? ws.SendJsonAsync(new { id = handler, type = "click", seq = stamped })
                : ws.SendJsonAsync(new { id = handler, type = "click" });
        }

        // The field as typed in, and whatever that was answered with read past.
        public async Task Type(string value)
        {
            await ws.SendJsonAsync(new { id = Handler("input"), type = "input", value });
            await Settled();
        }

        // The field left with that value in it: its change, which is what asks the rule.
        public Task Leave(string value) => ws.SendJsonAsync(new { id = Handler("change"), type = "change", value });

        private string Handler(string on) => Regex.Match(
            _html, $"data-rask-on-{on}=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;

        public async Task<List<string>> Settled()
        {
            var frames = await ws.SettledAsync();
            frames.ForEach(Note);
            return frames;
        }

        // The next frame, which the test asserts is not an ack: nothing has been acked this early.
        public async Task<string> NextOfAny()
        {
            var frame = await ws.ReceiveTextAsync();
            Assert.False(IsAck(frame), "the ack came before the page it answers was whole");
            Note(frame);
            return frame;
        }

        public async Task<string> Next()
        {
            var frame = await ws.ReceiveUntilAsync(static f => !IsAck(f), "a frame");
            Note(frame);
            return frame;
        }

        public async Task<string> Until(string text)
        {
            string frame;
            do
            {
                frame = await Next();
            }
            while (!frame.Contains(text, StringComparison.Ordinal));

            return frame;
        }

        // Every frame sent before the ack of the event stamped seq.
        public async Task<List<string>> UntilAck(int seq)
        {
            var frames = new List<string>();
            while (await ws.ReceiveTextAsync() is var frame && !IsAckOf(frame, seq))
            {
                Assert.False(IsAck(frame), "an ack nobody asked for");
                Note(frame);
                frames.Add(frame);
            }

            return frames;
        }

        private static bool IsAck(string frame) => frame.StartsWith("{\"type\":\"ack\"", StringComparison.Ordinal);

        private static bool IsAckOf(string frame, int seq) =>
            string.Equals(frame, $"{{\"type\":\"ack\",\"seq\":{seq.ToString(CultureInfo.InvariantCulture)}}}", StringComparison.Ordinal);

        private void Note(string frame)
        {
            using var doc = JsonDocument.Parse(frame);
            if (doc.RootElement.TryGetProperty("html", out var page))
            {
                _html = page.GetString()!;
            }

            var pager = Regex.Match(frame, "pager at \\d+", RegexOptions.None, TimeSpan.FromSeconds(1)).Value;
            var rows = Regex.Match(frame, "row [a-z0-9]+-3", RegexOptions.None, TimeSpan.FromSeconds(1)).Value;
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  frame {Encoding.UTF8.GetByteCount(frame)} B history={LiveFrames.HistoryUrl(frame)} [{pager}] [{rows}] loading={frame.Contains("id=\\\"loading", StringComparison.Ordinal)} saving={frame.Contains(">saving<", StringComparison.Ordinal)}"));
        }

        public async ValueTask DisposeAsync()
        {
            // Whatever is still held is let go, so no dispatch outlives the host that ran it.
            Store.Release();
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
                // The server may already have closed it.
            }

            ws.Dispose();
            host.Dispose();
        }
    }
}
