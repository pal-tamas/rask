using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class HelloMessageTests
{
    [Fact]
    public async Task Hello_UnknownSessionId_SendsSessionUnknownPayload()
    {
        using var host = RaskTestHost.Create<TestApp>();
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);

        await ws.SendJsonAsync(new { type = "hello", session = "no-such-id" });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        Assert.Equal("session", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("unknown", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Hello_ValidSessionId_TriggersFreshRender()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var initial = await host.Http.GetAsync("/start");
        var sessionId = ExtractSessionId(await initial.Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        Assert.True(doc.RootElement.TryGetProperty("html", out _));
    }

    [Fact]
    public async Task Hello_MissingSessionField_ConnectionStaysOpen_NoPayload()
    {
        using var host = RaskTestHost.Create<TestApp>();
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);

        await ws.SendJsonAsync(new { type = "hello" });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300));

        Assert.Null(text);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    private static string ExtractSessionId(string html)
    {
        var match = Regex.Match(html, "data-rask-root=\"([^\"]+)\"");
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }
}
