using Microsoft.Playwright;

namespace Rask.Testing;

/// <summary>
///     Waits for a Rask page to settle in a Playwright test: <c>await page.WaitForRask();</c> after a click, a fill, a
///     navigation — then assert what is on the page, with nothing left on its way.
/// </summary>
/// <remarks>
///     <para>
///         Reads the runtime's own mark. While the page is settling, Rask's client runtime (both the Server and the
///         WebAssembly host) keeps <c>data-rask-busy</c> on <c>&lt;html&gt;</c>: an event sent and not answered, its
///         render not on the page yet, a navigation in flight, a frame received and not applied, a value typed and
///         not sent, the connection being opened again, the app booting. The mark goes on in the same task as the
///         click or key that caused it, so a wait that starts once Playwright's click has returned never looks
///         before it.
///     </para>
///     <para>
///         Not covered: the app's own timers, and a render the server pushes that has not reached the browser yet —
///         a push that arrives later is a new busy period. A page that pushes a render every second still settles:
///         each push is applied in the task it arrives in.
///     </para>
/// </remarks>
public static class PageSettling
{
    // A page without the Rask runtime carries no mark, so it is settled at once.
    private const string Settled = "() => !document.documentElement.hasAttribute('data-rask-busy')";

    /// <summary>Waits until the page has settled: every event answered and on the page, nothing on its way.</summary>
    /// <param name="page">The page under test.</param>
    /// <param name="timeout">How long to wait; the page's default timeout when null (Playwright's 30 seconds unless set).</param>
    /// <returns>A task that completes once the page has settled.</returns>
    /// <exception cref="TimeoutException">The page was still settling when <paramref name="timeout" /> ran out.</exception>
    public static Task WaitForRask(this IPage page, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.MainFrame.WaitForRask(timeout);
    }

    /// <summary>Waits until the Rask page in <paramref name="frame" /> has settled.</summary>
    /// <param name="frame">The frame under test: a Rask app in an iframe.</param>
    /// <param name="timeout">How long to wait; the page's default timeout when null (Playwright's 30 seconds unless set).</param>
    /// <returns>A task that completes once the frame has settled.</returns>
    /// <exception cref="TimeoutException">The frame was still settling when <paramref name="timeout" /> ran out.</exception>
    public static async Task WaitForRask(this IFrame frame, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var options = new FrameWaitForFunctionOptions();
        if (timeout is { } limit)
        {
            options.Timeout = (float)limit.TotalMilliseconds;
        }

        var settled = await frame.WaitForFunctionAsync(Settled, null, options).ConfigureAwait(false);
        await settled.DisposeAsync().ConfigureAwait(false);
    }
}
