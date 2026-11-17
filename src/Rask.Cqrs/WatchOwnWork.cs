using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     Asks for the <see cref="WorkReported" />s of the work its subscriber enqueued — all of it, or one
///     <paramref name="Lane" />'s. What <c>Ui.WorkNotices</c> and <c>Ui.WorkList</c> subscribe to.
/// </summary>
/// <remarks>
///     It carries no user: who is asking is read on the server, where the subscription opens, by the watch policy Rask.Jobs
///     registers — which admits a signed-in user alone and <see cref="Admit">stamps</see> them on it, so the subscription
///     hears their work in their tenant and nobody else's. Without Rask.Jobs there is no policy, and nobody may open it.
/// </remarks>
/// <param name="Lane">The lane to watch, or null for every lane.</param>
internal sealed record WatchOwnWork(string? Lane) : ISubscription<WorkReported>
{
    /// <summary>What <see cref="CqrsRegistry" /> holds for it: its policy is the one Rask.Jobs registers, and none refuses.</summary>
    internal static readonly SubscriptionRegistration Registration = new(
        typeof(WorkReported),
        static (s, e) => ((WatchOwnWork)s).Matches((WorkReported)e),
        static (sp, s, ct) => CqrsRegistry.CanWatch(sp, (WatchOwnWork)s, ct));

    private readonly Lock _gate = new();
    private (Guid User, Guid? Tenant)? _admitted;

    /// <summary>Stamps who was admitted: from here on it hears <paramref name="user" />'s work in <paramref name="tenant" />.</summary>
    internal void Admit(Guid user, Guid? tenant)
    {
        lock (_gate)
        {
            _admitted = (user, tenant);
        }
    }

    /// <inheritdoc />
    public bool Matches(WorkReported e)
    {
        ArgumentNullException.ThrowIfNull(e);
        (Guid User, Guid? Tenant)? admitted;
        lock (_gate)
        {
            admitted = _admitted;
        }

        return admitted is { } who
            && e.Owner == who.User
            && e.Tenant == who.Tenant
            && (Lane is null || string.Equals(e.Lane, Lane, StringComparison.Ordinal));
    }

    // Two asking for the same lane are the same subscription: who was admitted is the server's stamp, not part of what
    // was asked for.
    /// <inheritdoc />
    public bool Equals(WatchOwnWork? other) => other is not null && string.Equals(Lane, other.Lane, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => Lane is null ? 0 : StringComparer.Ordinal.GetHashCode(Lane);
}
