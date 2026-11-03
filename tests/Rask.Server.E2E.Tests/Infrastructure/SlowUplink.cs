using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     What the page sends the server leaves late, in order, while the test says so: a line slower than a page that
///     is drawn anew every few dozen ms, with no load on the machine needed.
/// </summary>
/// <remarks>
///     What a loaded runner, or a reader on a slow line, does to a press by chance — it arrives many pages after the
///     one it was read from — done on purpose, every run.
/// </remarks>
internal static class SlowUplink
{
    /// <summary>Puts the delay in the page, before it loads anything. Nothing is late until <see cref="SlowAsync" />.</summary>
    public static Task Install(IPage page) => page.AddInitScriptAsync(
        """
        window.__uplinkDelay = 0;
        const send = WebSocket.prototype.send;
        WebSocket.prototype.send = function (data) {
            if (window.__uplinkDelay <= 0) return send.call(this, data);
            const socket = this;
            setTimeout(function () { if (socket.readyState === WebSocket.OPEN) send.call(socket, data); }, window.__uplinkDelay);
        };
        """);

    /// <summary>From now on everything the page sends leaves <paramref name="milliseconds" /> late.</summary>
    public static Task SlowAsync(IPage page, int milliseconds) =>
        page.EvaluateAsync("ms => { window.__uplinkDelay = ms; }", milliseconds);
}
