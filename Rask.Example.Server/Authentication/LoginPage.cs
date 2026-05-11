using System.Security.Claims;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Example.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using static Rask.Core.Tags;

namespace Rask.Example.Server.Authentication;

[Route("/login")]
[AllowAnonymous]
public sealed class LoginPage(AuthSignIn auth) : Component
{
    private static readonly Dictionary<string, string> _users = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alice"] = "password",
        ["bob"] = "password"
    };

    private readonly LoginModel _model = new();
    private bool _error;
    private bool _submitting;

    [QueryParam("returnUrl")] public string? ReturnUrl { get; set; }

    public override Component Render() =>
        Div(Class: "row justify-content-center", Children:
        [
            Div(Class: "col-md-5", Children:
            [
                H1(Class: "h3 mb-4", Children: ["Sign in"]),
                _error
                    ? Div(Class: "alert alert-danger", Children: ["Invalid username or password."])
                    : Fragment(),
                Form(Model: _model, OnValidSubmitAsync: HandleSubmitAsync, Children:
                [
                    Div(Class: "mb-3", Children:
                    [
                        Label(For: "username", Class: "form-label", Children: ["Username"]),
                        Input(Bind: () => _model.Username, Name: "username", Id: "username",
                            Class: "form-control", Required: true, Autofocus: true),
                        ValidationMessage(For: () => _model.Username, Class: "invalid-feedback d-block")
                    ]),
                    Div(Class: "mb-3", Children:
                    [
                        Label(For: "password", Class: "form-label", Children: ["Password"]),
                        Input(Bind: () => _model.Password, Type: "password", Name: "password", Id: "password",
                            Class: "form-control", Required: true),
                        ValidationMessage(For: () => _model.Password, Class: "invalid-feedback d-block")
                    ]),
                    Button(Type: "submit", Class: "btn btn-primary", Disabled: _submitting,
                        Children: [_submitting ? "Signing in…" : "Sign in"])
                ]),
                P(Class: "form-text mt-3 mb-0", Children:
                [
                    "Try ", Strong(Children: ["alice"]), " / ", Strong(Children: ["password"]),
                    " or ", Strong(Children: ["bob"]), " / ", Strong(Children: ["password"]), "."
                ])
            ])
        ]);

    private async Task HandleSubmitAsync(LoginModel model)
    {
        _submitting = true;
        _error = false;
        try
        {
            var name = model.Username?.Trim() ?? "";
            var pwd = model.Password ?? "";
            if (!_users.TryGetValue(name, out var expected) || expected != pwd)
            {
                _error = true;
                return;
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, name),
                    new Claim(ClaimTypes.NameIdentifier, name)
                ],
                CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await auth.SignInAsync(principal, ReturnUrl ?? "/");
        }
        finally
        {
            _submitting = false;
        }
    }
}
