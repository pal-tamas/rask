using System.Security.Claims;

namespace Rask.Core.Authentication;

public sealed class AnonymousUserProvider : IUserProvider
{
    private static readonly ClaimsPrincipal _anonymous = new(new ClaimsIdentity());

    public ClaimsPrincipal Current => _anonymous;

    public event Action? Changed
    {
        add { }
        remove { }
    }
}
