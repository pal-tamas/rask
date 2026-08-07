using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shop.Features.Auth;

[Route("login")]
[AllowAnonymous]
public sealed partial class LoginPage(IAuthSignIn auth, ICredentialStore creds) : Component
{
    private readonly LoginModel _model = new();
    private string? _error;

    [QueryParam] public string? ReturnUrl { get; set; }

    protected override Component? Render() =>
        Div(Class: "welcome-card")[
            H1()["Sign in"],
            _error is null ? null : Div(Style: "color:#b00020")[_error],
            // Async submit uses the generated OnValidSubmitAsync sibling (like Button's OnClickAsync).
            Form(_model, OnValidSubmitAsync: SubmitAsync)[
                Div()[Label("username")["Username"], Input(() => _model.Username, Id: "username")],
                Div()[Label("password")["Password"], Input(() => _model.Password, Id: "password", Type: InputType.Password)],
                Button("submit")["Sign in"]
            ],
            P()["Try alice / password (user) or root / password (admin)."]
        ];

    private async Task SubmitAsync(LoginModel m)
    {
        var claims = creds.Validate(m.Username, m.Password);
        if (claims is null)
        {
            _error = "Invalid username or password.";
            return;
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await auth.SignInAsync(new ClaimsPrincipal(identity), returnUrl: ReturnUrl ?? "/members");
    }
}
