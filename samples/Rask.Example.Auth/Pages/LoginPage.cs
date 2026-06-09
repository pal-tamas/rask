using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Pages;

[Route("login")]
[AllowAnonymous]
public sealed class LoginPage(IAuthSignIn auth, ICredentialStore creds) : Component
{
    private readonly LoginModel _model = new();
    private string? _error;

    // The cookie middleware appends ?ReturnUrl= on its challenge redirect; QueryParam binds it (case-insensitive).
    [QueryParam] public string? ReturnUrl { get; set; }

    protected override RenderResult Render() =>
        Div(Id: "login", Style: "max-width:22rem;margin:3rem auto;font-family:system-ui")[
            H1()["Sign in"],
            _error is null
                ? (Child)Fragment()
                : Div(Id: "login-error", Style: "color:#b00020;margin-bottom:.5rem")[_error],
            Form(_model, OnValidSubmitAsync: SubmitAsync)[
                Div(Style: "margin-bottom:.5rem")[
                    Label("username")["Username"],
                    Input(() => _model.Username, Id: "username")
                ],
                Div(Style: "margin-bottom:.75rem")[
                    Label("password")["Password"],
                    Input(() => _model.Password, Id: "password", Type: "password")
                ],
                Button("submit", Id: "login-submit")["Sign in"]
            ],
            P(Style: "color:#666;font-size:.85rem")["Try alice / password (user) or root / password (admin)."]
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
        // SignInAsync drives the redeem handshake; the returnUrl lands the user back on the protected page.
        await auth.SignInAsync(new ClaimsPrincipal(identity), returnUrl: ReturnUrl ?? "/members");
    }
}
