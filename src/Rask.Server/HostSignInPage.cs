using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.Server;

/// <summary>
///     The sign-in page the host says is its own, for an app with no page of its own at <c>/login</c>: the
///     <see cref="CookieAuthenticationOptions.LoginPath" /> of the default challenge scheme when that is a cookie
///     scheme, else <see cref="RaskServerOptions.SignInPath" />.
/// </summary>
/// <remarks>
///     Rask cannot tell whether an address outside its routes is served by anyone, so it sends a reader only where the
///     host has said its sign-in page is — as the cookie handler's own challenge does — never to a guess.
/// </remarks>
internal sealed record HostSignInPage(string Path, string ReturnUrlParameter)
{
    private const string RaskReturnUrlParameter = "returnUrl";

    /// <summary>The page's address with the way back: <paramref name="returnUrl" /> as the browser addresses it.</summary>
    public string For(string returnUrl) => Path + "?" + ReturnUrlParameter + "=" + Uri.EscapeDataString(returnUrl);

    /// <summary>The host's sign-in page, or null when its setup names none.</summary>
    public static async ValueTask<HostSignInPage?> FindAsync(IServiceProvider services)
    {
        if (await OfCookieSchemeAsync(services).ConfigureAwait(false) is { } cookie)
        {
            return cookie;
        }

        return services.GetService<RaskServerOptions>()?.SignInPath is { Length: > 0 } path
            ? new HostSignInPage(path, RaskReturnUrlParameter)
            : null;
    }

    private static async ValueTask<HostSignInPage?> OfCookieSchemeAsync(IServiceProvider services)
    {
        if (services.GetService<IAuthenticationSchemeProvider>() is not { } schemes
            || await schemes.GetDefaultChallengeSchemeAsync().ConfigureAwait(false) is not { } scheme
            || !typeof(CookieAuthenticationHandler).IsAssignableFrom(scheme.HandlerType)
            || services.GetService<IOptionsMonitor<CookieAuthenticationOptions>>()?.Get(scheme.Name) is not { } cookie
            || !cookie.LoginPath.HasValue)
        {
            return null;
        }

        return new HostSignInPage(cookie.LoginPath.Value, cookie.ReturnUrlParameter);
    }
}
