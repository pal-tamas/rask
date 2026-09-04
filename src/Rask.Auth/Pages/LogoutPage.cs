using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Auth.Pages;

/// <summary>
/// The built-in sign-out page, at <c>/logout</c>.
/// </summary>
/// <remarks>
/// It asks rather than acting. Signing out on GET would mean any cross-site <c>&lt;img src="/logout"&gt;</c>
/// could end a visitor's session — mild as attacks go, but free to prevent: the button runs a handler
/// over the app's own channel, which nothing cross-site can reach.
/// </remarks>
[AllowAnonymous]
[Route("logout")]
public sealed partial class LogoutPage(IAuth auth, IUserProvider users) : AuthPage
{
    /// <summary>Where to land after signing out. Sanitised to a local URL before use.</summary>
    [QueryParam]
    public string? ReturnUrl { get; set; }

    /// <inheritdoc />
    protected override Component? Content =>
        users.Current.Identity?.IsAuthenticated == true
            ? Fragment[
                H1["Sign out"],
                P[$"You are signed in as {users.Current.Identity.Name}."],
                Button.Type("button").Id("logout-submit").OnClickAsync(SignOutAsync)["Sign out"]
            ]
            : Fragment[
                H1["Signed out"],
                P.Class("rask-auth-note")["You are not signed in. ", NavLink.Href(Routes.LoginPage())["Sign in"], "."]
            ];

    private Task SignOutAsync() => auth.SignOutAsync(ReturnUrl);
}
