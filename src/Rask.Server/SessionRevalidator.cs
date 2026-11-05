using System.ComponentModel;
using System.Security.Claims;

namespace Rask.Server;

/// <summary>
///     What <c>AddSessionRevalidator&lt;T&gt;()</c> takes: derive from <see cref="SessionRevalidator{TUser}" />,
///     never from this.
/// </summary>
/// <remarks>
///     The user's type erased, so the host keeps an app's users without knowing their type. Nothing outside Rask
///     can derive from it: its abstract members are internal.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class SessionRevalidator
{
    /// <summary>Called by <see cref="SessionRevalidator{TUser}" />'s constructor.</summary>
    protected SessionRevalidator()
    {
    }

    /// <summary>The user as the app's store has them now, or <see langword="null" /> when there is no such user.</summary>
    internal abstract Task<object?> LoadUser(string userId, CancellationToken cancellationToken);

    /// <summary>The principal the session acts under, given the user it belongs to.</summary>
    internal abstract ClaimsPrincipal? PrincipalOf(object user, ClaimsPrincipal principal);
}
