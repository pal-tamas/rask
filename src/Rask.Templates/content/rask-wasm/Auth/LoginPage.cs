using Microsoft.AspNetCore.Authorization;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Company.RaskWasm;

[Route("login")]
[AllowAnonymous]
public sealed class LoginPage(JwtLoginService login) : Component
{
    private readonly LoginModel _model = new();
    private string? _error;

    [QueryParam] public string? ReturnUrl { get; set; }

    protected override Component? Render() =>
        Div(Style: "max-width:22rem;margin:3rem auto;font-family:system-ui")[
            H1()["Sign in"],
            _error is null ? null : Div(Style: "color:#b00020")[_error],
            Form(_model, OnValidSubmitAsync: SubmitAsync)[
                Div()[Label("username")["Username"], Input(() => _model.Username, Id: "username")],
                Div()[Label("password")["Password"], Input(() => _model.Password, Id: "password", Type: InputType.Password)],
                Button("submit", Id: "login-submit")["Sign in"]
            ]
        ];

    private async Task SubmitAsync(LoginModel m)
    {
        if (!await login.LoginAsync(m.Username, m.Password, ReturnUrl))
        {
            _error = "Invalid username or password.";
        }
    }
}
