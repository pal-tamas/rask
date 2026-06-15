using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Features;

[Route("login")]
[AllowAnonymous]
public sealed class LoginPage(IAuthSignIn auth, ICredentialStore creds) : Component
{
    private readonly LoginModel _model = new();
    private string? _error;

    // The cookie middleware appends ?ReturnUrl= on its challenge redirect; QueryParam binds it (case-insensitive).
    [QueryParam] public string? ReturnUrl { get; set; }

    protected override RenderResult Render() =>
        Div(Id: "login", Class: "card shadow-sm mx-auto", Style: "max-width:24rem")[
            Div(Class: "card-body")[
                H1(Class: "h3 card-title mb-3")["Sign in"],
                _error is null
                    ? (Child)Fragment()
                    : Div(Id: "login-error", Class: "alert alert-danger py-2")[_error],
                Form(_model, OnValidSubmitAsync: SubmitAsync)[
                    Div(Class: "mb-3")[
                        Label("username", Class: "form-label")["Username"],
                        Input(() => _model.Username, Id: "username", Class: "form-control")
                    ],
                    Div(Class: "mb-3")[
                        Label("password", Class: "form-label")["Password"],
                        Input(() => _model.Password, Id: "password", Type: "password", Class: "form-control")
                    ],
                    Button("submit", Id: "login-submit", Class: "btn btn-primary w-100")["Sign in"]
                ],
                P(Class: "text-muted small mt-3 mb-0")[
                    "Try alice / password (user) or root / password (admin)."]
            ]
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
        await auth.SignInAsync(new ClaimsPrincipal(identity), ReturnUrl ?? "/members");
    }
}
