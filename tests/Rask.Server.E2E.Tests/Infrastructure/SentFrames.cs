using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     The socket's outgoing frames, counted in the page: a count read "now" is the count now, where one kept
///     on the test's side of the wire trails the page by however long the event took to arrive.
/// </summary>
internal static class SentFrames
{
    /// <summary>Starts the count, before the page loads anything.</summary>
    public static Task Count(IPage page) => page.AddInitScriptAsync(
        """
        window.__sent = 0;
        const send = WebSocket.prototype.send;
        WebSocket.prototype.send = function (data) { window.__sent++; return send.call(this, data); };
        """);

    /// <summary>How many frames the page has sent since it loaded.</summary>
    public static Task<int> SoFarAsync(IPage page) => page.EvaluateAsync<int>("() => window.__sent");
}
