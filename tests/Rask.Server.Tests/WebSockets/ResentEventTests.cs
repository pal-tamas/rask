using System.Text.Json;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     The browser sends again, on its next connection, every event no ack answered (rask.ts, `unacked`). Some of
///     them reached the session before the old connection died, and those run once.
/// </summary>
public class ResentEventTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_click_sent_again_on_the_next_connection_runs_once(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<TestApp>();
        var page = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var session = MarkupAssert.SessionId(page);
        var bump = MarkupAssert.FirstHandlerId(page);
        await using (var first = await LiveTestConnection.OpenAsync(host, transport, session))
        {
            await first.SettledAsync();
            await first.SendJsonAsync(new { id = bump, type = "click", seq = 1 });
            await ReadUntilAckAsync(first);
        }

        await using var next = await LiveTestConnection.OpenAsync(host, transport, session, sent: 1);
        await next.SettledAsync();
        await next.SendJsonAsync(new { id = bump, type = "click", seq = 1 });
        var resent = await ReadUntilAckAsync(next);
        await next.SendJsonAsync(new { id = bump, type = "click", seq = 2 });
        var fresh = await ReadUntilAckAsync(next);

        Assert.Equal(1, resent.Acked);
        Assert.Empty(resent.Renders);
        Assert.Contains("count=2", Assert.Single(fresh.Renders));
    }

    [Fact]
    public async Task A_batch_sent_again_runs_none_of_its_events_twice()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var page = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var bump = MarkupAssert.FirstHandlerId(page);
        await using var ws = await LiveTestConnection.OpenAsync(host, LiveTransportKind.WebSocket, MarkupAssert.SessionId(page));
        await ws.SettledAsync();
        var batch = new { type = "batch", events = new object[] { new { id = bump, seq = 1 }, new { id = bump, seq = 2 } } };

        await ws.SendJsonAsync(batch);
        var first = await ReadUntilAckAsync(ws);
        await ws.SendJsonAsync(batch);
        var again = await ReadUntilAckAsync(ws);

        Assert.Contains("count=2", Assert.Single(first.Renders));
        Assert.Empty(again.Renders);
        Assert.Equal(2, again.Acked);
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
}
