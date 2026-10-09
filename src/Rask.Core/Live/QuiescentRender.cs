namespace Rask.Core.Live;

/// <summary>
///     Renders in waves until the page's async work has settled, or a budget runs out.
/// </summary>
/// <remarks>
///     <para>
///         A component that loads its data in <c>OnMount</c> renders its placeholder first and its
///         data only once the hook resolves. Rendering once and serving that is how a page ships
///         "Loading…" as the whole document a crawler sees. This renders, waits for the work that render
///         started, renders again, and repeats — because resolved data mounts new components, which start
///         work of their own that a single wait would miss entirely.
///     </para>
///     <para>
///         <b>Host-agnostic on purpose.</b> What differs between hosts is how a page is rendered and what
///         makes its work impossible to finish — not the shape of the loop. Both arrive as callbacks, so
///         the same waves drive a server's first response and a build-time prerender of an app that has
///         no server at all.
///     </para>
/// </remarks>
public static class QuiescentRender
{
    /// <summary>
    ///     Bounds a pathological render whose every wave starts new work, so the response cannot grow
    ///     without limit.
    /// </summary>
    public const int DefaultMaxWaves = 16;

    /// <summary>
    ///     How many waves a render asked for from inside a wave can earn. One settles a page that changes its
    ///     layout as it mounts; the second covers what that render mounted in turn. A component that asks
    ///     again on every render would otherwise cost the whole wave cap on every request.
    /// </summary>
    public const int MaxRequestedWaves = 2;

    /// <summary>
    ///     Drives <paramref name="renderWave" /> until nothing is pending, the budget expires, or
    ///     <paramref name="maxWaves" /> is reached.
    /// </summary>
    /// <param name="renderWave">
    ///     Renders the page and returns its markup. The argument is <c>publishOnly</c>: <c>false</c> for
    ///     the first wave and <c>true</c> for every wave after it.
    ///     <para>
    ///         <b>Honouring it is mandatory.</b> A re-render that is not publish-only re-fires
    ///         <c>OnRendered</c> on every component that already rendered, so each wave multiplies the
    ///         lifecycle callbacks of the one before it.
    ///     </para>
    /// </param>
    /// <param name="budget">
    ///     How long the waves may take in total — one deadline for the whole render, not per wave.
    /// </param>
    /// <param name="isBlocked">
    ///     Asked before each wait: does something make the pending work impossible to finish here?
    ///     Waiting then buys nothing and costs the whole budget. A server answering a <c>GET</c> passes
    ///     "is a JavaScript call queued", because that call completes only once a socket exists. Omitted,
    ///     nothing is ever considered blocked.
    /// </param>
    /// <param name="maxWaves">Wave cap. Defaults to <see cref="DefaultMaxWaves" />.</param>
    /// <param name="renderRequested">
    ///     Asked after a wave that left no work pending: did something ask for a render while it ran? A page
    ///     that names itself to its layout as it mounts does — the layout was rendered a moment earlier, so
    ///     the markup in hand is one render behind it. Such a wave is followed by another, at most
    ///     <see cref="MaxRequestedWaves" /> times, and never counts as timed out. Omitted, only pending work
    ///     earns another wave.
    /// </param>
    /// <param name="cancellationToken">
    ///     Abandons the render. A wait in progress stops at once and <see cref="OperationCanceledException" />
    ///     is thrown rather than a result returned: markup that stopped because its caller went away is
    ///     neither settled nor timed out, and reporting it as either would invite someone to serve it.
    ///     A render running in the background — a page re-rendered to refresh a stored copy — passes the
    ///     host's shutdown token here, so stopping the host does not wait out the rest of the budget.
    /// </param>
    /// <returns>The final markup, whether it settled, and how many extra waves it took.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    public static async Task<QuiescentRenderResult> Run(
        Func<bool, string> renderWave,
        TimeSpan budget,
        Func<bool>? isBlocked = null,
        int maxWaves = DefaultMaxWaves,
        Func<bool>? renderRequested = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderWave);
        cancellationToken.ThrowIfCancellationRequested();

        using var quiescence = QuiescenceScope.Begin();
        var html = renderWave(false);

        var deadline = DateTime.UtcNow + budget;
        var waves = 0;
        var requestedWaves = 0;

        while (true)
        {
            if (quiescence.TrySnapshotPending(out var batch))
            {
                if (isBlocked?.Invoke() == true
                    || !await Settled(batch, quiescence, deadline, waves < maxWaves, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            else if (requestedWaves++ >= MaxRequestedWaves || waves >= maxWaves || renderRequested?.Invoke() != true)
            {
                // Nothing to wait for, and nothing the last wave mounted asked it to run again.
                break;
            }

            html = renderWave(true);
            waves++;
        }

        return new QuiescentRenderResult(html, quiescence.TimedOut, waves);
    }

    // Waits for one wave's work. False, with the scope marked timed out, when the budget or the wave cap is spent.
    private static async Task<bool> Settled(
        Task[] batch, QuiescenceScope quiescence, DateTime deadline, bool wavesLeft, CancellationToken cancellationToken)
    {
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero || !wavesLeft)
        {
            quiescence.MarkTimedOut();
            return false;
        }

        try
        {
            await Task.WhenAll(batch).WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            quiescence.MarkTimedOut();
            return false;
        }
    }
}
