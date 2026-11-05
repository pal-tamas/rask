using System.Security.Claims;

namespace Rask.Server;

/// <summary>
///     A live session's question — is its user still signed in, and with which rights? — answered from the app's
///     OWN user type: say how a user is loaded and what principal they act under, and register it with
///     <c>AddSessionRevalidator&lt;T&gt;()</c>.
/// </summary>
/// <typeparam name="TUser">
///     What is loaded for a user: the app's user row, or the few fields of it the principal is built from.
/// </typeparam>
/// <remarks>
///     <para>
///         The two halves are asked apart, so the load can be kept. Before every event of a live page Rask finds the
///         principal's user id (<see cref="ClaimTypes.NameIdentifier" />, or <see cref="ClaimTypes.Name" /> when there
///         is none), <see cref="Load" />s that user — or takes the one it kept within <c>.Keep(…)</c> — and asks
///         <see cref="Principal" /> who the session acts as now. A principal with no user id has ended.
///     </para>
///     <code>
/// builder.Services.AddSessionRevalidator&lt;AccountCheck&gt;().Keep(5.Seconds).ForgetOnSave&lt;User&gt;();
///
/// internal sealed class AccountCheck : SessionRevalidator&lt;User&gt;
/// {
///     protected override async Task&lt;User?&gt; Load(string userId, CancellationToken cancellationToken = default) =&gt;
///         await User.Find(Guid.Parse(userId), cancellationToken);
///
///     protected override ClaimsPrincipal? Principal(User user, ClaimsPrincipal principal) =&gt;
///         !user.Active ? null
///         : user.IsAdmin == principal.IsInRole("admin") ? principal
///         : user.ToPrincipal(principal);
/// }
///     </code>
///     <para>
///         A singleton: what it needs from the container (a context factory, a clock) comes through its constructor.
///     </para>
/// </remarks>
public abstract class SessionRevalidator<TUser> : SessionRevalidator
    where TUser : class
{
    /// <summary>The user as the app's store has them now.</summary>
    /// <param name="userId">The principal's <see cref="ClaimTypes.NameIdentifier" /> (or <see cref="ClaimTypes.Name" />).</param>
    /// <param name="cancellationToken">Cancels the read: the page's client went away.</param>
    /// <returns>The user, or <see langword="null" /> when there is none any more — which ends the session's sign-in.</returns>
    /// <remarks>
    ///     Kept for the <c>.Keep(…)</c> given at registration, for every session of that user, so it must depend on
    ///     the user alone. What depends on the principal — a token's expiry, the tenant it chose — belongs in
    ///     <see cref="Principal" />.
    /// </remarks>
    protected abstract Task<TUser?> Load(string userId, CancellationToken cancellationToken = default);

    /// <summary>The principal the session acts under from now, given the user as <see cref="Load" /> found them.</summary>
    /// <param name="user">The user, as loaded or kept.</param>
    /// <param name="principal">The principal the session holds.</param>
    /// <returns>
    ///     <paramref name="principal" /> itself when nothing changed — which costs the host nothing; a principal with
    ///     other claims when their rights changed, which the session acts under and renders the page again for; or
    ///     <see langword="null" /> when they may no longer be signed in (disabled, a lapsed token), which sends the
    ///     page to the sign-in page.
    /// </returns>
    /// <remarks>Asked at every event, with nothing kept: it costs what it computes.</remarks>
    protected abstract ClaimsPrincipal? Principal(TUser user, ClaimsPrincipal principal);

    internal sealed override async Task<object?> LoadUser(string userId, CancellationToken cancellationToken) =>
        await Load(userId, cancellationToken).ConfigureAwait(false);

    internal sealed override ClaimsPrincipal? PrincipalOf(object user, ClaimsPrincipal principal) =>
        Principal((TUser)user, principal);
}
