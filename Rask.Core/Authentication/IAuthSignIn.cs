using System.Security.Claims;

namespace Rask.Core.Authentication;

public interface IAuthSignIn
{
    Task SignInAsync(ClaimsPrincipal principal, string? returnUrl = null, string? scheme = null);
    Task SignOutAsync(string? returnUrl = null, string? scheme = null);
}
