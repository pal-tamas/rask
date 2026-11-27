using Microsoft.AspNetCore.Http;

namespace Rask.Auth;

/// <summary>The client a sign-in attempt is counted against, as Rask's own endpoints count it.</summary>
public static class AuthClient
{
    /// <summary>
    /// The client <paramref name="context" />'s request comes from: its address, or for IPv6 the /64 it is one of. What
    /// <see cref="IBearerSignIn.SignIn" /> is handed, so an app's own sign-in endpoint is throttled exactly as
    /// <c>/api/auth/login</c> is.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>The client, or <see langword="null" /> when the request carries no address.</returns>
    /// <remarks>
    /// Behind a proxy this is the proxy's address unless the app runs <c>UseForwardedHeaders</c>, which is the ordinary
    /// ASP.NET arrangement for learning the caller's.
    /// </remarks>
    public static string? Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return AuthThrottle.ClientOf(context.Connection.RemoteIpAddress);
    }
}
