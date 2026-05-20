using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class AsyncValidationDispatchTests
{
    // Mirrors the failing E2E test Validation_AsyncDemo_ShowsCheckingThenTakenMessage:
    // OnInput "admin" then OnChange (blur). The async validator delays 20ms and then adds
    // "Already taken.". The post-handler render emitted after the OnChange must contain
    // that message and must not still contain the in-flight "Checking..." indicator.
    [Fact]
    public async Task AsyncValidator_PostHandlerFrame_ShowsMessage_AndNoIndicator()
    {
        using var host = RaskTestHost.Create<AsyncValidationApp>();
        var initial = await host.Http.GetAsync("/");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(initialHtml);
        var inputId = ExtractAttr(initialHtml, "data-rask-on-input");
        var changeId = ExtractAttr(initialHtml, "data-rask-on-change");
        Assert.NotNull(inputId);
        Assert.NotNull(changeId);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        // Discard the recovery render the dispatcher emits right after socket attach.
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = inputId, value = "admin" });
        // OnInput is synchronous (the field isn't touched yet, so StringSetHandler
        // doesn't trigger validation); a single post-handler frame should land.
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = changeId, value = "admin" });

        // Drain every frame the dispatcher emits for this handler — typically the mid-await
        // ("Checking...") plus the post-handler. The last frame on the wire must reflect the
        // terminal state.
        string? last = null;
        while (true)
        {
            var frame = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(500));
            if (frame is null)
            {
                break;
            }

            last = frame;
        }

        Assert.NotNull(last);
        var html = JsonDocument.Parse(last!).RootElement.GetProperty("html").GetString()!;
        Assert.Contains("Already taken.", html);
        Assert.DoesNotContain("Checking...", html);
    }

    private static string ExtractSessionId(string html) =>
        Regex.Match(html, "data-rask-root=\"([^\"]+)\"").Groups[1].Value;

    private static string? ExtractAttr(string html, string attr)
    {
        var marker = attr + "=\"";
        var i = html.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        var start = i + marker.Length;
        var end = html.IndexOf('"', start);
        return end < 0 ? null : html.Substring(start, end - start);
    }
}
