namespace Rask.Data;

/// <summary>
///     The tenant the application's resolver named for one request, or for a live session as of its last event.
/// </summary>
/// <remarks>
///     <para>
///         Scoped: a request asks once. A live session has a DI scope of its own that outlives every request, and
///         asks again at each event it takes (<see cref="AskAgain" />) — a handler, a navigation, a connection
///         attaching — so a user who chose another tenant in another tab works in it from this tab's next action.
///         Between events the answer is held: a render the server starts itself has no request to ask about.
///     </para>
///     <para>
///         Asked on first use, and <see cref="Capture" /> is that first use made early: the host calls it as a
///         request starts and as a session opens, while the request the resolver wants to read is still there.
///         Background work with a recorded tenant never asks at all — an explicit <c>Tenant.Use</c> wins before
///         this is consulted.
///     </para>
///     <para>
///         A resolver that throws is asked again next time rather than remembered as "no tenant": a failure
///         must not turn into an empty database.
///     </para>
/// </remarks>
internal sealed class ResolvedTenant(TenantResolver resolver, IServiceProvider scope)
{
    private readonly Lock _gate = new();
    private Guid? _tenant;
    private bool _asked;
    private bool _asking;

    /// <summary>The tenant the resolver named for this scope, or <see langword="null" /> when it named none.</summary>
    /// <exception cref="InvalidOperationException">The resolver read the tenant it was asked to supply.</exception>
    internal Guid? Tenant
    {
        get
        {
            lock (_gate)
            {
                if (!_asked)
                {
                    _tenant = Ask();
                    _asked = true;
                }

                return _tenant;
            }
        }
    }

    /// <summary>
    ///     Asks the resolver again and keeps the new answer: a live session does at each event it takes, so the
    ///     tenant it works in is the one its user has now. True when the answer is another than the one held.
    /// </summary>
    /// <remarks>A resolver that throws leaves nothing held, and is asked again by whatever reads the tenant next.</remarks>
    internal bool AskAgain()
    {
        lock (_gate)
        {
            var had = _asked;
            var before = _tenant;
            _asked = false;
            _tenant = Ask();
            _asked = true;

            return had && before != _tenant;
        }
    }

    /// <summary>Asks now, so the answer is the one given while this scope's request was in flight.</summary>
    internal static void Capture(IServiceProvider scope) => _ = For(scope)?.Tenant;

    /// <summary>The holder in <paramref name="scope" />, or null when the app registered no resolver.</summary>
    internal static ResolvedTenant? For(IServiceProvider? scope) =>
        (ResolvedTenant?)scope?.GetService(typeof(ResolvedTenant));

    private Guid? Ask()
    {
        // The lock is re-entrant, so a resolver that reads a tenant-scoped table arrives back here on the
        // same thread — and would otherwise ask itself for ever.
        if (_asking)
        {
            throw new InvalidOperationException(
                "The tenant resolver registered with AddRaskTenant read the current tenant, which is the " +
                "thing it is being asked for. Resolve the tenant from the request — a host name, a header, a " +
                "route value — or from a table that is not tenant-scoped, inside Tenant.Across() if it must be.");
        }

        _asking = true;
        try
        {
            return resolver.Resolve(scope);
        }
        finally
        {
            _asking = false;
        }
    }
}
