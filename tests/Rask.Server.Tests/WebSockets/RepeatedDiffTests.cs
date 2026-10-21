using Rask.Core;
using Rask.Core.Components;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // a test page; its <head> is not what is under test

namespace Rask.Server.Tests.WebSockets;

// Eszaf #16: a row moved down and then straight back up stayed down. The two replies were the same bytes — a swap of
// two neighbours is one move between the same two slots, and every attribute that follows the slot is put back the
// same way — and the session dropped the second as a repeat of the frame it had just sent. A page is the same page
// sent twice; a diff is a change, and the same change twice is not nothing.
public class RepeatedDiffTests
{
    [Fact]
    public async Task Swapping_two_keyed_rows_back_sends_the_second_diff_although_its_bytes_repeat_the_first()
    {
        using var host = RaskTestHost.Create<SwapBackApp>();
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var handlerId = MarkupAssert.FirstHandlerId(html);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);

        var replies = new List<List<string>>();
        for (var swap = 0; swap < 4; swap++)
        {
            await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
            replies.Add(Diffs(await ws.SettledAsync()));
        }

        Assert.All(replies, static reply => Assert.Contains("[7,", Assert.Single(reply), StringComparison.Ordinal));
        Assert.Equal(Ops(replies[1][0]), Ops(replies[2][0]));
    }

    private static List<string> Diffs(List<string> frames) =>
        [.. frames.Where(static frame => frame.Contains("\"kind\":\"diff\"", StringComparison.Ordinal))];

    private static string Ops(string frame) => frame[frame.IndexOf("\"ops\"", StringComparison.Ordinal)..frame.LastIndexOf(']')];
}

/// <summary>Two keyed rows and the button that swaps them.</summary>
public sealed partial class SwapBackApp : Component
{
    private readonly List<string> _rows = ["a", "b"];

    protected override Component? HeadAssets => Markup.Title["swap back"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Button.OnClick(() => _rows.Reverse())["swap"],
        Ul[_rows.Select(row => Li.Key(row)[row])]
    ];
}
