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
        window.__kit = { sent: 0, received: 0, asked: 0, acked: 0, moves: 0, x: -1, y: -1, asking: 0 };
        // A move reaches the page in a frame of the browser's own, after the call that made it has returned
        // (a passive listener does not hold it up): counted here, so a gesture can wait for it to arrive.
        window.addEventListener('pointermove', e => {
            const kit = window.__kit;
            kit.moves++;
            kit.x = e.clientX;
            kit.y = e.clientY;
        }, { capture: true, passive: true });
        // The runtime asks whether the browser's credential changed before the first frame after a pause
        // (rask-auth-watch.ts) and holds what the page sends until the answer is back: counted until the frames it
        // held have left, which is a task after the answer was read.
        const fetch0 = window.fetch;
        window.fetch = function (input, init) {
            const url = typeof input === 'string' ? input : (input && input.url) || '';
            if (url.indexOf('/_rask/auth/who') < 0) {
                return fetch0.call(this, input, init);
            }
            const kit = window.__kit;
            kit.asking++;
            const done = () => {
                const after = new MessageChannel();
                after.port1.onmessage = () => { kit.asking--; };
                after.port2.postMessage(0);
            };
            return fetch0.call(this, input, init).then(response => {
                if (!response.ok) { done(); return response; }
                const json = response.json.bind(response);
                response.json = () => json().finally(done);
                return response;
            }, failure => { done(); throw failure; });
        };
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

    /// <summary>
    ///     Moves the pointer to (<paramref name="x" />, <paramref name="y" />) and resolves once the page has heard
    ///     the move. Chromium hands a move to the page in its next frame of the real clock, after the call that made
    ///     it has returned: a step given its beat before then would be heard in the next step's frame, the two
    ///     taken as one, and the count would depend on how fast the machine is.
    /// </summary>
    public static async Task MoveAsync(IPage page, float x, float y)
    {
        var heard = await page.EvaluateAsync<double[]>("() => [window.__kit.moves, window.__kit.x, window.__kit.y]");
        await page.Mouse.MoveAsync(x, y);
        // A pointer already there (to the pixel the page is told) moves nowhere, and one taken off the page is
        // over nothing of it: neither is told.
        if ((Math.Abs(heard[1] - x) < 1 && Math.Abs(heard[2] - y) < 1) || !OnPage(page, x, y))
        {
            return;
        }

        // At the place it was taken to: the browser also sends a move of its own where the pointer already is
        // when what is under it changes, and that one says nothing about this.
        await page.WaitForFunctionAsync(
            "([n, x, y]) => window.__kit.moves > n && Math.abs(window.__kit.x - x) < 1 && Math.abs(window.__kit.y - y) < 1",
            new[] { heard[0], x, y }, new() { Timeout = AnswerMs, PollingInterval = 20 });
    }

    private static bool OnPage(IPage page, float x, float y) =>
        page.ViewportSize is not { } viewport || (x >= 0 && y >= 0 && x < viewport.Width && y < viewport.Height);

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
            "() => window.__kit.asking === 0 && window.__kit.acked >= window.__kit.asked", null,
            new() { Timeout = AnswerMs, PollingInterval = 20 });
}
