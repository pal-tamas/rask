using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Hosting.Shared;
using Rask.Server.Authentication;

namespace Rask.Server;

/// <summary>Registers the session cookie a sign-in with the app's own token is written into.</summary>
public static class CookieSessionExtensions
{
    /// <summary>
    ///     Keeps a session cookie for an app whose authentication scheme reads its own token from a cookie — a JWT bearer
    ///     scheme, or a handler that looks a session row up. A live handler that has checked the credentials calls
    ///     <c>Auth.SignInWithToken(token, returnUrl)</c>; Rask hands the token to the browser through a single-use,
    ///     30-second ticket redeemed by the page's own origin, writes it into this cookie, and loads the page again.
    ///     A sign-out clears it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The cookie's name, domain and lifetime, after the <c>Rask:CookieSession</c> section.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddRaskCookieSession(
        this IServiceCollection services, Action<CookieSessionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRaskOptions<CookieSessionOptions>(
            "Rask:CookieSession", static (section, o) => section.Bind(o), configure, static o => o.Validate());
        services.TryAddSingleton(static sp => new CookieSession(
            sp.GetRequiredService<CookieSessionOptions>(), sp.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }
}
