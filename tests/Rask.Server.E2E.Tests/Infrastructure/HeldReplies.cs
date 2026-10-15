using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     The socket's replies wait in the page while the test says so, and land in order once it lets go: a line
///     slower than the reader, with no clock in it.
/// </summary>
/// <remarks>
///     What a loaded runner does to a journey by chance — a press read from a page the server has already
///     rendered past — done on purpose, every run.
/// </remarks>
internal static class HeldReplies
{
    /// <summary>Puts the hold in the page, before it loads anything. Nothing is held until <see cref="HoldAsync" />.</summary>
    public static Task Install(IPage page) => page.AddInitScriptAsync(
        """
        window.__holding = false;
        const held = [];
        window.__release = function () {
            window.__holding = false;
            for (const deliver of held.splice(0)) deliver();
        };
        const listen = WebSocket.prototype.addEventListener;
        WebSocket.prototype.addEventListener = function (type, listener, options) {
            if (type !== "message") return listen.call(this, type, listener, options);
            return listen.call(this, type, function (e) {
                if (window.__holding) held.push(() => listener(e)); else listener(e);
            }, options);
        };
        """);

    /// <summary>From now on what the server says waits.</summary>
    public static Task HoldAsync(IPage page) => page.EvaluateAsync("() => { window.__holding = true; }");

    /// <summary>Everything held lands, in the order it came, and nothing is held any more.</summary>
    public static Task ReleaseAsync(IPage page) => page.EvaluateAsync("() => window.__release()");
}
