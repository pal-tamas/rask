namespace Rask.Data;

/// <summary>
///     The tenant the application's resolver named for one request or one live session, asked once and kept.
/// </summary>
/// <remarks>
///     <para>
///         Scoped, which is the whole of "held for the session": a live session has a DI scope of its own that
///         outlives every request, so the answer given when it opened is the answer for as long as it lives —
///         it does not move because a later message arrived on another socket or with no request at all.
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
