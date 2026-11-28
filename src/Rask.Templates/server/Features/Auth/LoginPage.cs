using Microsoft.AspNetCore.Authorization;

namespace Company.RaskServer.Features.Auth;

// /login is where the route guard sends a visitor who is not signed in; one who is, [Guest] sends home.
// Ui.SignIn is the whole form: the address, the password, the refusal under it, and the way back to ?returnUrl=
// (an address of this site only). .Logo("/logo.svg") puts your mark above the name.
[AllowAnonymous]
[Guest]
[Route("/login")]
public sealed partial class LoginPage : Component
{
    protected override string? PageTitle => "Sign in";

    protected override Component? Render() =>
        Ui.SignIn.Title("Company.RaskServer").Remember().Passkey()[
            Ui.Text["No account yet? ", Ui.Link.Href(Routes.RegisterPage())["Create one"], "."],
            Ui.Text[Ui.Link.Href(Routes.ForgotPasswordPage())["Forgotten your password?"]]
        ];
}
