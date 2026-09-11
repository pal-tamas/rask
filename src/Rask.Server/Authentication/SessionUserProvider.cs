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
    private ClaimsPrincipal _current = new(new ClaimsIdentity());
    private int _reads;

    /// <summary>
    ///     The session's principal. An unauthenticated <see cref="ClaimsPrincipal" /> until something signs
    ///     in — never <see langword="null" />, so <c>Current.Identity?.IsAuthenticated</c> is the check.
    /// </summary>
    public ClaimsPrincipal Current
    {
        get
        {
            Interlocked.Increment(ref _reads);
            return _current;
        }
    }

    /// <summary>
    ///     How many times <see cref="Current" /> has been read — by a component, or by anything else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read before and after a render, the difference says whether that render's markup can
    ///         depend on who is signed in. A page that never asked cannot, so one stored copy of it is
    ///         correct for every visitor; a page that did — a navigation bar greeting the user, an
    ///         <c>Authorize</c> gate — is correct only for the principal it was rendered for.
    ///     </para>
    ///     <para>
    ///         Counted here rather than detected in the render walk because this is the door the framework's
    ///         own components — <c>Authorize</c> among them — go through to learn the user, and it costs a
    ///         single increment on a property read that is already rare. <see cref="Set" /> and
    ///         <see cref="Clear" /> use the field, so the framework replacing the principal is not mistaken
    ///         for a page reading it.
    ///     </para>
    ///     <para>
    ///         <b>It is not the only door, and a caller must not read it as one.</b> An app can register an
    ///         <see cref="IUserProvider" /> of its own, whose reads never reach this count — so a caller
    ///         that finds the resolved provider is not this instance has to assume the user was read. And a
    ///         component can go straight to the request through <c>IHttpContextAccessor</c>, which nothing
    ///         here can see at all.
    ///     </para>
    /// </remarks>
    internal int ReadCount => Volatile.Read(ref _reads);

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
        var prev = _current;
        _current = user;
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
        if (_current.Identity?.IsAuthenticated == true)
        {
            Set(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }
}
