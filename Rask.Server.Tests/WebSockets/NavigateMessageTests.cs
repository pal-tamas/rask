using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class NavigateMessageTests
{
    [Fact]
    public async Task Navigate_UpdatesRouteState_AndSendsPayloadWithHistoryPush()
    {
        await using var fixture = await ConnectedSession.Connect<TestApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/destination", query = "" });

        var text = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("push", history.GetProperty("action").GetString());
        Assert.Equal("/destination", history.GetProperty("url").GetString());

        var routeState = fixture.Session.Services.GetRequiredService<RouteState>();
        Assert.Equal("/destination", routeState.Path);
    }

    [Fact]
    public async Task Navigate_WithReplaceTrue_SendsHistoryReplace()
    {
        await using var fixture = await ConnectedSession.Connect<TestApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/x", query = "", replace = true });

        var text = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        using var doc = JsonDocument.Parse(text!);
        Assert.Equal("replace", doc.RootElement.GetProperty("history").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Navigate_EmptyPath_NoPayload()
    {
        await using var fixture = await ConnectedSession.Connect<TestApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "" });
        var text = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300));

        Assert.Null(text);
    }

    [Fact]
    public async Task Navigate_QueryWithoutLeadingQuestion_NormalisesUrl()
    {
        await using var fixture = await ConnectedSession.Connect<TestApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/x", query = "a=1&b=2" });

        var text = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        using var doc = JsonDocument.Parse(text!);
        Assert.Equal("/x?a=1&b=2", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }
}
