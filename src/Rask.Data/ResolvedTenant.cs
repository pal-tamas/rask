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
    private bool _lost;

    /// <summary>The tenant the resolver named for this scope, or <see langword="null" /> when it named none.</summary>
    /// <exception cref="InvalidOperationException">The resolver read the tenant it was asked to supply.</exception>
    /// <exception cref="MissingTenantException">
    ///     The resolver named a tenant for this session and stopped naming one with nothing about its user or its
    ///     page having changed (<see cref="AskAgain" />).
    /// </exception>
    internal Guid? Tenant
    {
        get
        {
            lock (_gate)
            {
                Ensure();

                return _lost ? throw LostWithItsRequest() : _tenant;
            }
        }
    }

    /// <summary>
    ///     Asks the resolver again and keeps the new answer: a live session does at each event it takes, so the
    ///     tenant it works in is the one its user has now. True when the answer is another than the one held.
    /// </summary>
    /// <param name="sameReader">
    ///     Whether the session's user and page are the ones the answer held was given for. A resolver that named a
    ///     tenant then and names none now has not been told anything new: it read something only the page's own
    ///     request carried, and from here on reading the tenant throws rather than quietly reading nothing.
    /// </param>
    /// <remarks>A resolver that throws leaves nothing held, and is asked again by whatever reads the tenant next.</remarks>
    internal bool AskAgain(bool sameReader)
    {
        lock (_gate)
        {
            var had = _asked;
            var before = _tenant;
            var wasLost = _lost;
            _asked = false;
            _lost = false;
            _tenant = Ask();
            _asked = true;

            // Lost stays lost for as long as the same reader goes on getting no answer.
            _lost = sameReader && _tenant is null && (wasLost || (had && before is not null));

            return had && before != _tenant;
        }
    }

    /// <summary>Asks now, so the answer is the one given while this scope's request was in flight.</summary>
    /// <remarks>Says nothing of an answer that was lost: whatever reads the tenant is told (<see cref="Tenant" />).</remarks>
    internal static void Capture(IServiceProvider scope) => For(scope)?.EnsureAsked();

    private void EnsureAsked()
    {
        lock (_gate)
        {
            Ensure();
        }
    }

    private void Ensure()
    {
        if (!_asked)
        {
            _tenant = Ask();
            _asked = true;
        }
    }

    private static MissingTenantException LostWithItsRequest() =>
        new("The tenant resolver registered with AddRaskTenant named a tenant when this page was opened and names " +
            "none now, for the same user on the same page. It read something only the page's own request carried — " +
            "a route value, a header — and a live page's events arrive on its connection's request, which is the " +
            "socket's. Read the page's address from RouteState (sp.GetRequiredService<RouteState>().Path), which " +
            "follows the page as it navigates, or the tenant from the signed-in user (Current.Principal).");

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
