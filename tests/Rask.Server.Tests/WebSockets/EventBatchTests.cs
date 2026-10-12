using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     The events one browser task produced arrive as one <c>batch</c> frame: their handlers run in order and
///     the page is rendered once — unless a render in between is something a handler could tell.
/// </summary>
/// <remarks>
///     Every test sends its last event with a <c>seq</c> and reads until the ack, which the server sends after the
///     batch's last render: what was read by then is everything the batch sent, with no waiting for quiet.
///     Asserts against the full-HTML payload, so each host is created with the diff codec off.
/// </remarks>
public class EventBatchTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task Sixty_events_in_one_batch_run_in_order_and_are_answered_with_one_render(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, transport);
        await using var connection = ws;
        var events = Enumerable.Range(0, EventBatchApp.Cells).Select(i => Event(page, $"hit{i}", seq: i + 1)).ToArray();

        await ws.SendJsonAsync(Batch(events));
        var (renders, acked) = await ReadUntilAckAsync(ws);

        var html = HtmlOf(Assert.Single(renders));
        Assert.Equal(Number(page, "walks") + 1, Number(html, "walks"));
        var stamps = Enumerable.Range(0, EventBatchApp.Cells).Select(i => Number(html, $"cell{i}")).ToArray();
        Assert.Equal(stamps.Order(), stamps);
        Assert.Equal(EventBatchApp.Cells, stamps.Distinct().Count());
        Assert.Equal(EventBatchApp.Cells, acked);
    }

    [Fact]
    public async Task The_same_events_sent_a_frame_each_are_each_rendered_as_they_always_were()
    {
        using var host = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        var renders = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            await ws.SendJsonAsync(Event(page, $"hit{i}", seq: i + 1));
            renders.AddRange((await ReadUntilAckAsync(ws)).Renders);
        }

        Assert.Equal(5, renders.Count);
        Assert.Equal(Number(page, "walks") + 5, Number(HtmlOf(renders[^1]), "walks"));
    }

    [Fact]
    public async Task Handlers_of_one_component_in_a_batch_each_see_the_render_of_the_one_before()
    {
        using var host = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        // `snap` closes over the count its render computed. Run before the render that follows `bump`, it would
        // log the count the page showed when the browser sent it; a frame each, it has always logged the new one.
        await ws.SendJsonAsync(Batch(Event(page, "bump"), Event(page, "double"), Event(page, "bump"), Event(page, "snap", seq: 1)));
        var (renders, _) = await ReadUntilAckAsync(ws);

        var html = HtmlOf(renders[^1]);
        Assert.Equal(3, Number(html, "count"));
        Assert.Contains("log=bdb[3];", html);
        Assert.Equal(4, renders.Count);
    }

    [Fact]
    public async Task A_handler_that_awaits_in_a_batch_still_renders_while_it_waits()
    {
        EventBatchApp.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        await ws.SendJsonAsync(Batch(Event(page, "hit0"), Event(page, "slow"), Event(page, "hit1", seq: 1)));
        var waiting = HtmlOf((await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(10)))!);
        EventBatchApp.Gate.TrySetResult();
        var (renders, _) = await ReadUntilAckAsync(ws);

        // The render made while the handler waits shows what it did so far, and the event before it.
        Assert.Contains("busy=True;", waiting);
        Assert.NotEqual("-", Text(waiting, "cell0"));
        Assert.Equal("-", Text(waiting, "cell1"));
        var done = HtmlOf(renders[^1]);
        Assert.Contains("busy=False;log=s;", done);
        Assert.NotEqual("-", Text(done, "cell1"));
    }

    [Fact]
    public async Task A_handler_that_navigates_in_a_batch_sends_its_address_at_once_and_the_rest_still_run()
    {
        using var host = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        await ws.SendJsonAsync(Batch(Event(page, "hit0"), Event(page, "go"), Event(page, "hit1", seq: 1)));
        var (renders, _) = await ReadUntilAckAsync(ws);

        Assert.Equal(2, renders.Count);
        using var navigation = JsonDocument.Parse(renders[0]);
        Assert.Equal("/elsewhere", navigation.RootElement.GetProperty("history").GetProperty("url").GetString());
        Assert.NotEqual("-", Text(HtmlOf(renders[0]), "cell0"));
        Assert.Equal("-", Text(HtmlOf(renders[0]), "cell1"));
        using var rest = JsonDocument.Parse(renders[1]);
        Assert.False(rest.RootElement.TryGetProperty("history", out _));
        Assert.NotEqual("-", Text(HtmlOf(renders[1]), "cell1"));
    }

    [Fact]
    public async Task A_handler_that_throws_in_a_batch_leaves_the_page_where_a_frame_each_leaves_it()
    {
        using var batched = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (batchedPage, batchedWs) = await OpenAsync(batched, LiveTransportKind.WebSocket);
        await using var batchedConnection = batchedWs;
        using var single = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (singlePage, singleWs) = await OpenAsync(single, LiveTransportKind.WebSocket);
        await using var singleConnection = singleWs;

        await batchedWs.SendJsonAsync(Batch(Event(batchedPage, "hit0"), Event(batchedPage, "boom"), Event(batchedPage, "bump", seq: 1)));
        var together = (await ReadUntilAckAsync(batchedWs)).Renders;
        var apart = new List<string>();
        foreach (var name in new[] { "hit0", "boom", "bump" })
        {
            await singleWs.SendJsonAsync(Event(singlePage, name, seq: 1));
            apart.AddRange((await ReadUntilAckAsync(singleWs)).Renders);
        }

        // The root boundary's page replaces the app, so the event after the throw finds no handler: either way.
        Assert.Contains("Application error", HtmlOf(apart[^1]));
        Assert.Contains("Application error", HtmlOf(together[^1]));
        Assert.Equal(2, apart.Count);
        Assert.Single(together);
    }

    [Fact]
    public async Task A_batch_whose_last_event_names_no_handler_still_renders_what_the_others_did()
    {
        using var host = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        await ws.SendJsonAsync(Batch(Event(page, "hit0"), Event(page, "hit1"), new { id = "no-such-handler", seq = 9 }));
        var (renders, acked) = await ReadUntilAckAsync(ws);

        var html = HtmlOf(Assert.Single(renders));
        Assert.NotEqual("-", Text(html, "cell0"));
        Assert.NotEqual("-", Text(html, "cell1"));
        Assert.Equal(9, acked);
    }

    [Fact]
    public async Task A_batch_skips_what_is_not_an_event_and_runs_the_rest()
    {
        using var host = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        await ws.SendRawAsync(
            $$"""{"type":"batch","events":[7,"x",null,{"type":"navigate","path":"/nowhere"},{"id":"{{Id(page, "hit0")}}","seq":1}]}""");
        var (renders, _) = await ReadUntilAckAsync(ws);

        Assert.NotEqual("-", Text(HtmlOf(Assert.Single(renders)), "cell0"));
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_batch_longer_than_the_client_ever_sends_ends_the_connection(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<EventBatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, transport);
        await using var connection = ws;
        var events = Enumerable.Repeat(Event(page, "hit0"), EventBatch.MaxEvents + 1).ToArray();

        await SendIgnoringCloseAsync(ws, Batch(events));
        var reason = await ws.TryReceiveCloseReasonAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("event batch", reason);
    }

    [Fact]
    public async Task Batches_count_their_events_against_the_frame_rate_cap()
    {
        using var host = RaskTestHost.Create<EventBatchApp>(
            diffMode: LiveDiffMode.DisabledFull, configureServer: o => o.MaxInboundFramesPerSecond = 100);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;
        var events = Enumerable.Repeat(Event(page, "hit0"), 60).ToArray();

        // Two frames, far under a hundred a second — carrying a hundred and twenty handlers between them.
        await SendIgnoringCloseAsync(ws, Batch(events));
        await SendIgnoringCloseAsync(ws, Batch(events));
        var reason = await ws.TryReceiveCloseReasonAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("event batch", reason);
    }

    [Fact]
    public async Task A_batch_is_one_dispatch_to_the_handler_backlog_however_many_events_it_carries()
    {
        using var host = RaskTestHost.Create<EventBatchApp>(
            diffMode: LiveDiffMode.DisabledFull, configureServer: o => o.MaxPendingHandlers = 4);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;
        var events = Enumerable.Range(0, EventBatchApp.Cells).Select(i => Event(page, $"hit{i}", seq: i + 1)).ToArray();

        await ws.SendJsonAsync(Batch(events));
        var (renders, acked) = await ReadUntilAckAsync(ws);

        Assert.Single(renders);
        Assert.Equal(EventBatchApp.Cells, acked);
    }

    private static async Task<(string Page, ILiveTestConnection Ws)> OpenAsync(RaskTestHost host, LiveTransportKind transport)
    {
        var page = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var ws = await LiveTestConnection.OpenAsync(host, transport, MarkupAssert.SessionId(page));
        return (page, ws);
    }

    private static object Batch(params object[] events) => new { type = "batch", events };

    private static object Event(string page, string button, int? seq = null) =>
        seq is { } n ? new { id = Id(page, button), type = "click", seq = n } : new { id = Id(page, button), type = "click" };

    private static string Id(string page, string button)
    {
        var match = Regex.Match(page, $"id=\"{button}\"[^>]*data-rask-on-click=\"([^\"]+)\"");
        Assert.True(match.Success, $"button '{button}' not found");
        return match.Groups[1].Value;
    }

    // A server that refuses the frame may close while it is still being written.
    private static async Task SendIgnoringCloseAsync(ILiveTestConnection ws, object frame)
    {
        try
        {
            await ws.SendJsonAsync(frame);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            // The close is what the test reads next.
        }
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

    private static string Text(string html, string field)
    {
        var match = Regex.Match(html, $"{field}=([^;]*);");
        Assert.True(match.Success, $"'{field}' not found");
        return match.Groups[1].Value;
    }

    private static int Number(string html, string field) =>
        int.Parse(Text(html, field), CultureInfo.InvariantCulture);
}
