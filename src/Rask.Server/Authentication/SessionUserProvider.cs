using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server.Authentication;

public sealed class SessionUserProvider : IUserProvider
{
    public ClaimsPrincipal Current { get; private set; } = new(new ClaimsIdentity());

    public event Action? Changed;

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
