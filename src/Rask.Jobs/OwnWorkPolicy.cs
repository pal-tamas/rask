using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     Who may open <see cref="WatchOwnWork" />: anyone signed in — and what they hear is stamped on it here, in their own
///     scope: their work in their tenant, and the work their tenant shares (<c>.ByTenant()</c>) of the kinds whose handler's
///     <c>[Authorize]</c> admits them, decided now (<see cref="WorkAccess" />) so each event is matched without a wait. Nobody
///     signed in is refused: a job enqueued by nobody is shown to nobody.
/// </summary>
/// <remarks>
///     Its constructor is not public, so the CQRS generator leaves it alone: <c>AddRaskJobs</c> registers it, and a registry
///     generated into Rask.Jobs would collide, through InternalsVisibleTo, with the one its tests generate (CS0436).
/// </remarks>
internal sealed class OwnWorkPolicy : IWatchPolicy<WatchOwnWork>
{
    private readonly IServiceProvider _scope;

    // The subscriber's own scope: who asks is read from it, as the board reads it, whatever is ambient where it opens.
    internal OwnWorkPolicy(IServiceProvider scope) => _scope = scope;

    /// <inheritdoc />
    public async Task<bool> CanWatch(WatchOwnWork subscription, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (await WorkAccess.Viewer(_scope, cancellationToken).ConfigureAwait(false) is not { } viewer)
        {
            return false;
        }

        subscription.Admit(viewer);
        return true;
    }
}
