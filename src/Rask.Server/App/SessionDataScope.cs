using Rask.Core.Live;
using Rask.Data;

namespace Rask;

/// <summary>
///     Makes a live session's services ambient for the data layer, so a read run by that session sees the
///     tenant its signed-in user belongs to.
/// </summary>
/// <remarks>
///     <para>
///         This tiny class exists because of the package graph, not because the idea is complicated.
///         <c>Rask.Server</c> owns the session and its DI scope but does not reference <c>Rask.Data</c>, and
///         should not: a web host that drags EF Core in for every app, including those that never touch the
///         data layer, is the coupling the package split exists to prevent. <c>Rask.Data</c> cannot reference
///         <c>Rask.Core</c> either. Rask.Server is the one place that sees both, so the contract lives
///         in Core, the call lives in Data, and the wiring lives here.
///     </para>
///     <para>
///         Registered only when the data battery is on, so a host without it resolves nothing and the render
///         path keeps a null check.
///     </para>
/// </remarks>
internal sealed class SessionDataScope : ISessionWorkScope
{
    /// <inheritdoc />
    /// <remarks>
    ///     The first entry is the session's initial render, inside the request that opened it — which is when an
    ///     app's tenant resolver (<c>AddRaskTenant</c>) is first asked. Each event the session takes asks it again
    ///     (<see cref="AskAgain" />); work the server starts itself runs in the tenant the last event named.
    /// </remarks>
    public IDisposable? Enter(IServiceProvider sessionServices) => Db.OpenScope(sessionServices);

    /// <summary>
    ///     Asks the app's resolver again for a session an event has just reached, with the session's services
    ///     ambient so it can read <c>Current.Principal</c>. True when it named another tenant than before.
    /// </summary>
    /// <param name="tenant">The session's holder of the resolver's answer.</param>
    /// <param name="sessionServices">The session's own service provider.</param>
    /// <param name="sameReader">Whether the session's user and page are the ones the last answer was for.</param>
    internal static bool AskAgain(ResolvedTenant tenant, IServiceProvider sessionServices, bool sameReader)
    {
        using var ambient = Db.UseScope(sessionServices);
        return tenant.AskAgain(sameReader);
    }

    /// <summary>
    ///     Reads the rows of the tenant and the user an event is about to run for, ahead of it, so
    ///     <c>Current.Tenant</c> in the event is a lookup. Completes at once, allocating nothing, when they are held.
    /// </summary>
    /// <param name="sessionServices">The session's own service provider.</param>
    /// <param name="cancellationToken">Ends the read with the event.</param>
    internal static ValueTask Prime(IServiceProvider sessionServices, CancellationToken cancellationToken) =>
        CurrentRows.Prime(sessionServices, cancellationToken);
}
