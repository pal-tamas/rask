using Rask.Core.Diagnostics;
using Rask.Core.ScopedAssets;

namespace Rask.Server;

/// <summary>
///     Repaints every live session when a scoped stylesheet or script a page may be showing changes — once, after a
///     <see cref="QuietPeriod" /> with no further change.
/// </summary>
/// <remarks>
///     <para>
///         Only a <see cref="ScopedAssetChangedEventArgs.Replaced" /> change repaints. A first registration is an
///         assembly's module initializer running as it loads — at startup, or lazily in the middle of a run in
///         production — and no page can be showing an asset of an assembly that had not loaded. Repainting every
///         session of the host for it was pure cost, paid by every reader at once.
///     </para>
///     <para>
///         Trailing-edge debounce: a multi-file edit raises one change per file, and a generation snapshot that
///         survives the quiet window means no newer change came — the last one wins and the sessions repaint once.
///     </para>
/// </remarks>
internal sealed class ScopedAssetRepaint(LiveSessionStore sessions)
{
    /// <summary>How long no further change must come before the sessions repaint.</summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(50);

    private long _generation;

    /// <summary>The <see cref="ScopedAssetRegistry.AssetChanged" /> handler: starts <see cref="Changed" /> and returns.</summary>
    public void OnAssetChanged(object? sender, ScopedAssetChangedEventArgs change) => _ = Changed(change);

    /// <summary>Repaints the sessions after the quiet period when <paramref name="change" /> replaced or dropped an asset.</summary>
    /// <returns>Completes when this change's repaint is done, or skipped.</returns>
    public Task Changed(ScopedAssetChangedEventArgs change)
    {
        if (!change.Replaced)
        {
            return Task.CompletedTask;
        }

        return RepaintWhenQuiet(Interlocked.Increment(ref _generation));
    }

    private async Task RepaintWhenQuiet(long generation)
    {
        await Task.Delay(QuietPeriod).ConfigureAwait(false);
        if (Interlocked.Read(ref _generation) != generation)
        {
            return;
        }

        try
        {
            await sessions.RerenderAll().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.HotReload", "Rask: debounced asset-change rerender failed", ex);
        }
    }
}
