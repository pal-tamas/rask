using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Rask.Core.Routing;
using Rask.Server.Authentication;

namespace Rask.Example.Auth.Jwt.Features;

[Route("login")]
[AllowAnonymous]
public sealed class LoginPage(
    ICredentialStore creds,
    JwtIssuer issuer,
    JwtValidator validator,
    ProtectedSessionStorage store,
    SessionUserProvider users,
    Navigator nav) : Component
{
    private readonly LoginModel _model = new();
    private string? _error;

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
        var ok = creds.Validate(m.Username, m.Password);
        if (ok is null)
        {
            _error = "Invalid username or password.";
            return;
        }

        // Issue the JWT and stash it encrypted in sessionStorage (decrypted only server-side). Then validate
        // it into a principal and set it on the live session — no URL token, no cookie, no JS-readable token.
        var jwt = issuer.Issue(ok.Value.Name, ok.Value.Roles);
        await store.SetAsync("rask.jwt", jwt);
        if (validator.Validate(jwt) is { } principal)
        {
            users.Set(principal);
        }

        nav.NavigateTo("/members");
    }
}
