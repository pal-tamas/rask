using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Wire;

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
                H1.Class("text-2xl font-bold")["Sign out"],
                P.Class("text-sm opacity-70")[$"You are signed in as {users.Current.Identity.Name}."],
                Div.Class("card-actions mt-2")[
                    Button
                        .Type("button")
                        .Id("logout-submit")
                        .Class("btn btn-primary btn-block")
                        .OnClick(SignOutAsync)["Sign out"]
                ]
            ]
            : Fragment[
                H1.Class("text-2xl font-bold")["Signed out"],
                P.Class("text-sm opacity-70")[
                    "You are not signed in. ",
                    NavLink.Href(Routes.LoginPage()).Class("link link-primary")["Sign in"],
                    "."]
            ];

    private Task SignOutAsync() => auth.SignOutAsync(ReturnUrl);
}
