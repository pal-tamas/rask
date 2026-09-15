using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Company.RaskServer.Features.Auth;

// It asks rather than acting: signing out on GET would let any cross-site <img src="/logout"> end a session.
[AllowAnonymous]
[Route("/logout")]
public sealed partial class LogoutPage(IAuth auth, IUserProvider users) : AuthPage
{
    [QueryParam]
    public string? ReturnUrl { get; set; }

    protected override Component? HeadAssets => Title["Sign out"];

    protected override Component? Content =>
        users.Current.Identity?.IsAuthenticated == true
            ? Fragment[
                H1.Class("text-2xl font-bold")["Sign out"],
                P.Class("text-sm opacity-70")[$"You are signed in as {users.Current.Identity.Name}."],
                Div.Class("card-actions mt-2")[
                    Button.Type("button").Id("logout-submit").Class("btn btn-primary btn-block").OnClick(SignOutAsync)["Sign out"]
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
