using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;
using static Rask.Server.Tests.WebSockets.HeardFieldTests;

namespace Rask.Server.Tests.WebSockets;

// The browser says when its tab goes out of sight and comes back (rask-visibility.ts), on the route every frame
// takes: what polls waits for it (PageVisibility).
public class PageVisibilityFrameTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_visibility_frame_holds_what_polls_until_the_tab_is_shown_again(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<ChatBoxApp>(diffMode: LiveDiffMode.DisabledFull);
        var (html, sessionId) = await StartAsync(host);
        await using var connection = await LiveTestConnection.OpenAsync(host, transport, sessionId);
        var look = Attr(html, "look", "data-rask-on-click");

        await connection.SendJsonAsync(new { type = "visibility", hidden = true });
        await connection.SendJsonAsync(new { id = look, type = "click" });
        var hidden = await connection.ReceiveUntilAsync(frame => frame.Contains("tab=hidden", StringComparison.Ordinal) || frame.Contains("tab=shown", StringComparison.Ordinal), "the look while hidden");
        await connection.SendJsonAsync(new { type = "visibility", hidden = false });
        await connection.SendJsonAsync(new { id = look, type = "click" });
        var shown = await connection.ReceiveUntilAsync(frame => frame.Contains("tab=shown", StringComparison.Ordinal), "the look once shown");

        Assert.Contains("tab=hidden", hidden, StringComparison.Ordinal);
        Assert.Contains("tab=shown", shown, StringComparison.Ordinal);
    }
}
