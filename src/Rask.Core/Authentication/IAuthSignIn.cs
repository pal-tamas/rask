using System.Security.Claims;

namespace Rask.Core.Authentication;

public interface IAuthSignIn
{
    /// <summary>Signs <paramref name="principal" /> in once the current handler finishes.</summary>
    /// <param name="principal">Who is signing in.</param>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    /// <param name="scheme">The authentication scheme, when the app has several.</param>
    /// <param name="persistent">Whether the session should outlive the browser session ("remember me").</param>
    Task SignInAsync(
        ClaimsPrincipal principal, string? returnUrl = null, string? scheme = null, bool persistent = false);

    /// <summary>Signs the current visitor out once the current handler finishes.</summary>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    /// <param name="scheme">The authentication scheme, when the app has several.</param>
    Task SignOutAsync(string? returnUrl = null, string? scheme = null);
}
