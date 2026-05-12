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
}
