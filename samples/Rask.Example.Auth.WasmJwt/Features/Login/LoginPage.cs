using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmJwt.Features;

[AllowAnonymous]
public sealed partial class LoginPage(JwtLoginService login) : Page
{
    protected override string Route => "login";

    private readonly LoginModel _model = new();
    private string? _error;

    [QueryParam] public string? ReturnUrl { get; set; }

    protected override Component? Render() =>
        Div.Id("login").Class("card shadow-sm mx-auto").Style("max-width:24rem")[
            Div.Class("card-body")[
                H1.Class("h3 card-title mb-3")["Sign in"],
                _error is null
                    ? null
                    : Div.Id("login-error").Class("alert alert-danger py-2")[_error],
                Form(_model, OnValidSubmitAsync: SubmitAsync)[
                    Div.Class("mb-3")[
                        Label.For("username").Class("form-label")["Username"],
                        Input.Bind(() => _model.Username).Id("username").Class("form-control")
                    ],
                    Div.Class("mb-3")[
                        Label.For("password").Class("form-label")["Password"],
                        Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Class("form-control")
                    ],
                    Button.Type("submit").Id("login-submit").Class("btn btn-primary w-100")["Sign in"]
                ],
                P.Class("text-muted small mt-3 mb-0")[
                    "Try alice / password (user) or root / password (admin)."]
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
