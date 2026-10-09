using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class NavigateMessageTests
{
    [Fact]
    public async Task Navigating_updates_the_route_state_and_sends_a_payload_with_a_history_push()
    {
        await using var fixture = await ConnectedSession.Connect<TestApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/destination", query = "" }, ct: TestContext.Current.CancellationToken);

        var text = await fixture.Ws.ReceiveTextAsync();

        using var doc = JsonDocument.Parse(text);
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("push", history.GetProperty("action").GetString());
        Assert.Equal("/destination", history.GetProperty("url").GetString());

        var routeState = fixture.Session.Services.GetRequiredService<RouteState>();
        Assert.Equal("/destination", routeState.Path);
    }

    [Fact]
    public async Task Navigating_with_replace_sends_a_history_replace()
    {
        await using var fixture = await ConnectedSession.Connect<TestApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/x", query = "", replace = true }, ct: TestContext.Current.CancellationToken);

        var text = await fixture.Ws.ReceiveTextAsync();

        using var doc = JsonDocument.Parse(text);
        Assert.Equal("replace", doc.RootElement.GetProperty("history").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Navigating_to_an_empty_path_sends_no_payload()
    {
        await using var fixture = await ConnectedSession.Connect<TestApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "" }, ct: TestContext.Current.CancellationToken);
        var frames = await fixture.Ws.SettledAsync();

        Assert.Empty(frames);
    }

    [Fact]
    public async Task A_query_without_a_leading_question_mark_is_normalised_into_the_url()
    {
        await using var fixture = await ConnectedSession.Connect<TestApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/x", query = "a=1&b=2" }, ct: TestContext.Current.CancellationToken);

        var text = await fixture.Ws.ReceiveTextAsync();

        using var doc = JsonDocument.Parse(text);
        Assert.Equal("/x?a=1&b=2", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }
}
