using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     The page's socket, watched from inside the page: how many frames went each way, and whether the server
///     has answered everything it was asked.
/// </summary>
/// <remarks>
///     A handler event carries a <c>seq</c> and the server acknowledges each once its render is sent
///     (<c>{type:"ack",seq}</c>). So "the page has been answered" is a fact read off the wire, not a time that
///     has passed: a count read once it holds is the same on a laptop and on a loaded two-core runner.
/// </remarks>
internal static class KitSocket
{
    // Long enough for a runner with everything else on it, and never what a count depends on.
    private const float AnswerMs = 60_000;

    /// <summary>Starts the watch, before the page loads anything.</summary>
    public static Task Install(IPage page) => page.AddInitScriptAsync(
        """
        window.__kit = { sent: 0, received: 0, asked: 0, acked: 0 };
        window.WebSocket = class extends WebSocket {
            constructor(...args) {
                super(...args);
                this.addEventListener('open', () => { window.__raskOpen = true; });
                this.addEventListener('message', e => {
                    const kit = window.__kit;
                    kit.received++;
                    try {
                        const frame = JSON.parse(e.data);
                        if (frame && frame.type === 'ack' && frame.seq > kit.acked) { kit.acked = frame.seq; }
                    } catch { /* not a frame of the runtime's */ }
                });
            }
            send(data) {
                const kit = window.__kit;
                kit.sent++;
                try {
                    const frame = JSON.parse(data);
                    for (const event of frame.type === 'batch' ? frame.events : [frame]) {
                        if (typeof event.seq === 'number' && event.seq > kit.asked) { kit.asked = event.seq; }
                    }
                } catch { /* not a frame of the runtime's */ }
                return super.send(data);
            }
        };
        """);

    /// <summary>The socket is open.</summary>
    public static Task OpenAsync(IPage page) =>
        page.WaitForFunctionAsync("() => window.__raskOpen === true", null, new() { Timeout = AnswerMs, PollingInterval = 20 });

    /// <summary>The frames this document has put on the socket and taken off it so far.</summary>
    public static async Task<KitFrames> CountAsync(IPage page)
    {
        var count = await page.EvaluateAsync<int[]>("() => [window.__kit.sent, window.__kit.received]");
        return new KitFrames(count[0], count[1]);
    }

    /// <summary>
    ///     Resolves once the server has answered every event the page sent it, and the answers are on the page:
    ///     the runtime hears a frame in the task this watch saw it in, and this returns in a later one.
    /// </summary>
    public static Task AnsweredAsync(IPage page) =>
        page.WaitForFunctionAsync(
            "() => window.__kit.acked >= window.__kit.asked", null, new() { Timeout = AnswerMs, PollingInterval = 20 });
}
