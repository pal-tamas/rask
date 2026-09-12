using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server.Authentication;

/// <summary>
///     Who the current live session belongs to, for the server host. Components read it through
///     <see cref="IUserProvider" /> — this is the implementation that holds the principal for the life of
///     the session and re-renders when it changes.
/// </summary>
/// <remarks>
///     This is the answer to "who is this", never on its own the answer to "may they". Authorize each
///     action where it happens: a principal held in a session says what the user signed in as, not what
///     the request in front of you is allowed to do.
/// </remarks>
public sealed class SessionUserProvider : IUserProvider
{
    /// <summary>
    ///     The session's principal. An unauthenticated <see cref="ClaimsPrincipal" /> until something signs
    ///     in — never <see langword="null" />, so <c>Current.Identity?.IsAuthenticated</c> is the check.
    /// </summary>
    public ClaimsPrincipal Current { get; private set; } = new(new ClaimsIdentity());

    /// <summary>
    ///     Raised when <see cref="Current" /> is replaced by a different principal, so UI that depends on
    ///     who is signed in re-renders. Setting the same instance again raises nothing.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    ///     Replaces the session's principal — sign-in, or a re-issued set of claims. Raises
    ///     <see cref="Changed" /> unless the same instance is passed back.
    /// </summary>
    /// <param name="user">The new principal.</param>
    /// <exception cref="ArgumentNullException"><paramref name="user" /> is <see langword="null" />. Use
    ///     <see cref="Clear" /> to sign out.</exception>
    public void Set(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var prev = Current;
        Current = user;
        if (!ReferenceEquals(prev, user))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    ///     Reset to an unauthenticated principal — explicit session invalidation on sign-out. Raises
    ///     <see cref="Changed" /> (via <see cref="Set" />) only when the session was actually
    ///     authenticated, so a redundant clear on an already-anonymous session is a no-op.
    /// </summary>
    public void Clear()
    {
        if (Current.Identity?.IsAuthenticated == true)
        {
            Set(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }
}
