using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server.Authentication;

public sealed class SessionUserProvider : IUserProvider
{
    private ClaimsPrincipal _current = new(new ClaimsIdentity());

    public ClaimsPrincipal Current => _current;

    public event Action? Changed;

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
}
