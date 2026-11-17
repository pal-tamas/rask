using Rask.Cqrs;
using Rask.Data;

namespace Rask.Background;

/// <summary>
///     Who may open <see cref="WatchOwnWork" />: anyone signed in — and what they hear is stamped on it here, in their own
///     scope, so it is their work in their tenant and nobody else's. Nobody signed in is refused: a job enqueued by nobody is
///     shown to nobody.
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
    public Task<bool> CanWatch(WatchOwnWork subscription, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (CurrentUser.UserIdIn(_scope) is not { } user)
        {
            return Task.FromResult(false);
        }

        subscription.Admit(user, Tenant.KeyIn(_scope));
        return Task.FromResult(true);
    }
}
