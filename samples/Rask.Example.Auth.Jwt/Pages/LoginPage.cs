using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Server.Authentication;

namespace Rask.Example.Auth.Jwt.Pages;

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

        nav.Navigate("/members");
    }
}
