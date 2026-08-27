namespace Rask.Core.Live;

/// <summary>
///     The outcome of a render driven to quiescence.
/// </summary>
/// <param name="Html">The markup as it stood when the last wave finished.</param>
/// <param name="TimedOut">
///     Whether a wave gave up before its work settled — the page is being served incomplete.
/// </param>
/// <param name="Waves">How many extra waves ran after the first render. Zero means it settled at once.</param>
public readonly record struct QuiescentRenderResult(string Html, bool TimedOut, int Waves);

/// <summary>
///     Renders in waves until the page's async work has settled, or a budget runs out.
/// </summary>
/// <remarks>
///     <para>
///         A component that loads its data in <c>OnMountAsync</c> renders its placeholder first and its
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
    /// <returns>The final markup, whether it settled, and how many extra waves it took.</returns>
    public static async Task<QuiescentRenderResult> RunAsync(
        Func<bool, string> renderWave,
        TimeSpan budget,
        Func<bool>? isBlocked = null,
        int maxWaves = DefaultMaxWaves)
    {
        ArgumentNullException.ThrowIfNull(renderWave);

        using var quiescence = QuiescenceScope.Begin();
        var html = renderWave(false);

        var deadline = DateTime.UtcNow + budget;
        var waves = 0;

        while (quiescence.TrySnapshotPending(out var batch))
        {
            if (isBlocked?.Invoke() == true)
            {
                break;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || waves >= maxWaves)
            {
                quiescence.MarkTimedOut();
                break;
            }

            try
            {
                await Task.WhenAll(batch).WaitAsync(remaining).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                quiescence.MarkTimedOut();
                break;
            }

            html = renderWave(true);
            waves++;
        }

        return new QuiescentRenderResult(html, quiescence.TimedOut, waves);
    }
}
