using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Authentication;

/// <summary>A page that signs in with the app's own token, as an app whose scheme reads a cookie does.</summary>
public sealed partial class TokenSignInTestApp(IAuthSignIn auth, IUserProvider users) : Component
{
    protected override Component? HeadAssets => Markup.Title["token-test"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Markup.P[$"user={(users.Current.Identity?.IsAuthenticated == true ? users.Current.Identity.Name : "anon")}"],
        Button.OnClick(() => auth.SignInWithToken(TokenCookieHandler.AliceToken, "/orders?page=2"))["token-sign-in"],
        Button.OnClick(() => auth.SignInWithToken(TokenCookieHandler.AliceToken, "https://evil.example/orders"))["token-sign-in-elsewhere"],
        Button.OnClick(() => auth.SignOut("/login"))["sign-out"]
    ];
}

/// <summary>
///     A scheme that reads the app's own token from the session cookie and can neither sign a principal in nor out —
///     what a JWT bearer scheme, or a handler that looks a session row up, is to Rask.
/// </summary>
internal sealed class TokenCookieHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "AppToken";
    public const string CookieName = "app-token";
    public const string AliceToken = "token-of-alice";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Cookies[CookieName] != AliceToken)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.NameIdentifier, "alice")], Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}
