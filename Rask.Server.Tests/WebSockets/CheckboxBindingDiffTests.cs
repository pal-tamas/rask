using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// In-process reproduction of the "Subscribe checkbox sticks after a few clicks" bug,
// exercising the real WS dispatch + render + diff path with a per-render JS invoke (so
// clicks ship diffs carrying jsInvokes, like the showcase's CodeSample-wrapped demos).
public class CheckboxBindingDiffTests
{
    [Fact]
    public async Task CheckboxChange_SetsModelToReportedState_AcrossManyClicks()
    {
        using var host = RaskTestHost.Create<CheckboxJsInvokeApp>();
        var initial = await host.Http.GetAsync("/");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = Regex.Match(initialHtml, "data-rask-root=\"([^\"]+)\"").Groups[1].Value;
        var changeId = Regex.Match(initialHtml, "data-rask-on-change=\"(h\\d+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(changeId), $"no change handler in: {initialHtml}");
        Assert.Contains("S=False", initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        async Task AssertEchoAsync(string sentValue, string expectedEcho, string label)
        {
            // The client reports the checkbox's actual checked state ("true"/"false"); the
            // model is SET to it (not toggled), so this sequence is deterministic regardless
            // of how many frames coalesce. The frame is a diff (UpdateText carries the new
            // "S=True"/"S=False" text) or full HTML — assert on the raw frame either way.
            await ws.SendJsonAsync(new { id = changeId, type = "change", value = sentValue });
            var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            Assert.False(string.IsNullOrEmpty(frame), $"no frame for {label}");
            Assert.Contains($"S={expectedEcho}", frame!);
        }

        await AssertEchoAsync("true", "True", "click 1");
        await AssertEchoAsync("true", "True", "click 1 repeated (idempotent)");
        await AssertEchoAsync("false", "False", "click 2");
        await AssertEchoAsync("true", "True", "click 3");
        await AssertEchoAsync("false", "False", "click 4");
    }
}
