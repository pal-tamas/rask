using System.Text.Json;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class AsyncValidationDispatchTests
{
    // Asserts against the `html` payload field — force the legacy full-HTML wire
    // shape (framework default is LiveDiffMode.Auto). SessionGracePeriod collection
    // serialises with the other DiffMode-touching test classes.

    // Mirrors the failing E2E test Validation_AsyncDemo_ShowsCheckingThenTakenMessage:
    // "admin" arrives on the field's OnChange. The async validator delays 20ms and then adds
    // "Already taken.". The post-handler render emitted after the OnChange must contain
    // that message and must not still contain the in-flight "Checking..." indicator.
    [Fact]
    public async Task An_async_validators_post_handler_frame_shows_the_message_and_no_indicator()
    {
        using var host = RaskTestHost.Create<AsyncValidationApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/", TestContext.Current.CancellationToken);
        var initialHtml = await initial.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var changeId = MarkupAssert.Attr(initialHtml, "data-rask-on-change");
        Assert.NotNull(changeId);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);

        await ws.SendJsonAsync(new { id = changeId, value = "admin" }, ct: TestContext.Current.CancellationToken);

        // Wait for the frame that carries the settled verdict rather than for a gap in the traffic: a loaded machine can
        // take longer than any gap to send the first (#1144), and can send one frame between the verdict landing and the
        // indicator clearing. What the page settles on is what the user sees.
        var verdict = await ws.ReceiveUntilAsync(
            f => f.Contains("Already taken.", StringComparison.Ordinal) && !f.Contains("Checking...", StringComparison.Ordinal),
            "the frame that carries the settled verdict");

        var html = JsonDocument.Parse(verdict).RootElement.GetProperty("html").GetString()!;
        Assert.Contains("Already taken.", html);
        Assert.DoesNotContain("Checking...", html);
    }
}
