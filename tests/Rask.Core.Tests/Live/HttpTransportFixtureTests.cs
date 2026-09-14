using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The Server runtime's HTTP fallback, driven in a Node subprocess: the event-stream parser, the POST batcher
///     and the choice between a WebSocket and HTTP.
/// </summary>
/// <remarks>
///     <para>
///         The fallback exists for the failure paths — a proxy that swallows the upgrade, a network that cuts
///         sockets, a server that is simply mid-redeploy — and those are exactly the paths a browser test cannot
///         produce on demand. So the parts that decide are written without the DOM and driven here with a fake
///         clock, fake storage and fake responses.
///     </para>
///     <para>
///         The rule under the most pressure is the chooser's: a socket that fails to open is NOT enough to pin a
///         tab to HTTP, because a server that is down fails the same way. Only an HTTP stream that opens where the
///         socket did not is remembered.
///     </para>
/// </remarks>
public sealed class HttpTransportFixtureTests
{
    private static JsonElement? Run() => NodeFixture.Run("HttpTransportFixture");

    [Fact]
    public void The_parser_reads_events_however_the_network_splits_them()
    {
        // No node on PATH — the fixture cannot run. Deliberately not a failure: node is not required to build or
        // test Rask, and the transport's end-to-end behaviour is covered by the server's HTTP transport tests.
        var result = Run();
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        var whole = root.GetProperty("whole");
        Assert.Equal(1, whole.GetArrayLength());
        Assert.Equal("message", whole[0].GetProperty("event").GetString());
        Assert.Equal("{\"type\":\"stream\",\"generation\":7}", whole[0].GetProperty("data").GetString());

        // Split mid-field, mid-value and between \r and \n: still exactly one event, with the data intact.
        var split = root.GetProperty("split");
        Assert.Equal(1, split.GetArrayLength());
        Assert.Equal("{\"type\":\"ack\"}", split[0].GetProperty("data").GetString());

        // The heartbeat comment dispatches nothing; a named event keeps its name; data lines join with a newline.
        var mixed = root.GetProperty("mixed");
        Assert.Equal(2, mixed.GetArrayLength());
        Assert.Equal("close", mixed[0].GetProperty("event").GetString());
        Assert.Equal("server-shutdown", mixed[0].GetProperty("data").GetString());
        Assert.Equal("one\ntwo", mixed[1].GetProperty("data").GetString());

        // A bare `data` line is an empty value — one event with empty data; a blank line alone is nothing.
        var degenerate = root.GetProperty("degenerate");
        Assert.Equal(1, degenerate.GetArrayLength());
        Assert.Equal(string.Empty, degenerate[0].GetProperty("data").GetString());
    }

    [Fact]
    public void Frames_sent_while_a_post_is_in_flight_wait_and_go_together_in_order()
    {
        var result = Run();
        if (result is null)
        {
            return;
        }

        var batching = result.Value.GetProperty("batching");

        // One POST at a time: the order a typing handler and the submit after it arrive in is the protocol's.
        Assert.Equal(1, batching.GetProperty("inFlightBeforeRelease").GetInt32());

        var bodies = batching.GetProperty("bodies");
        Assert.Equal(2, bodies.GetArrayLength());
        Assert.Equal("[{\"id\":\"a\"}]", bodies[0].GetString());
        Assert.Equal("[{\"id\":\"b\"},{\"id\":\"c\"},{\"id\":\"d\"}]", bodies[1].GetString());
        Assert.Equal(0, batching.GetProperty("pendingAfter").GetInt32());
    }

    [Fact]
    public void A_refused_post_stops_the_batcher_rather_than_posting_into_a_refused_session()
    {
        var result = Run();
        if (result is null)
        {
            return;
        }

        var refusal = result.Value.GetProperty("refusal");

        Assert.Equal(1, refusal.GetProperty("refusedWith").GetArrayLength());
        Assert.Equal(409, refusal.GetProperty("refusedWith")[0].GetInt32());
        Assert.Equal(1, refusal.GetProperty("posts").GetInt32());
    }

    [Fact]
    public void Only_an_http_stream_that_opened_where_a_socket_did_not_is_remembered()
    {
        var result = Run();
        if (result is null)
        {
            return;
        }

        var choosing = result.Value.GetProperty("choosing");
        string Choice(string name) => choosing.GetProperty(name).GetString()!;

        // A fresh tab tries the socket.
        Assert.Equal("ws", Choice("firstChoice"));

        // A failed socket makes HTTP the candidate — but if HTTP fails too, the server was down, and the tab goes
        // back to the socket instead of being pinned to the slower transport for the rest of its life.
        Assert.Equal("http", Choice("duringOutage"));
        Assert.Equal("ws", Choice("afterOutage"));

        // A failed socket and an HTTP stream that opened: remembered, and the reloaded tab starts on HTTP.
        Assert.Equal("http", Choice("rememberedValue"));
        Assert.Equal("http", Choice("reloaded"));
    }

    [Fact]
    public void Sockets_cut_off_early_three_times_count_and_blips_or_deliberate_closes_do_not()
    {
        var result = Run();
        if (result is null)
        {
            return;
        }

        var choosing = result.Value.GetProperty("choosing");
        string Choice(string name) => choosing.GetProperty(name).GetString()!;

        Assert.Equal("http", Choice("afterThreeEarlyCloses"));

        // A socket that stayed up a while before dropping resets the count: that is a reconnect, not a network
        // that cuts sockets.
        Assert.Equal("ws", Choice("afterBlips"));

        // A sign-in or an expired session closes the socket on purpose, and is never evidence against it.
        Assert.Equal("ws", Choice("afterDeliberateCloses"));
    }
}
