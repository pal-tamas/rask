using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Data;

namespace Rask;

/// <summary>The scope's own signed-in user (the session user provider every scope has), set for background work.</summary>
internal sealed class ScopeUser(IUserProvider users) : IScopeUser
{
    public void SignIn(ClaimsPrincipal user)
    {
        if (users is ISettableUserProvider settable)
        {
            settable.Set(user);
        }
    }
}
