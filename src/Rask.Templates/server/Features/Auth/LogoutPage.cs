using Microsoft.AspNetCore.Authorization;

namespace Company.RaskServer.Features.Auth;

// It asks rather than acting: signing out on GET would let any cross-site <img src="/logout"> end a session.
[AllowAnonymous]
[Route("/logout")]
public sealed partial class LogoutPage(IAuth auth, IUserProvider users) : AuthPage
{
    [QueryParam]
    public string? ReturnUrl { get; set; }

    protected override string? PageTitle => "Sign out";

    protected override Component? Content =>
        users.Current.Identity?.IsAuthenticated == true
            ? [
                Title("Sign out"),
                Ui.Text[$"You are signed in as {users.Current.Identity.Name}."],
                Ui.Button.Primary.Id("logout-submit").Class("w-full").OnClick(SignOut)["Sign out"]
            ]
            : [
                Title("Signed out"),
                Ui.Text["You are not signed in. ", Ui.Link.Href(Routes.LoginPage())["Sign in"], "."]
            ];

    private Task SignOut() => auth.SignOut(ReturnUrl);
}
