using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;
using Rask.Wire;

namespace Company.RaskServer.Features.Auth;

public sealed class SignInModel
{
    public string Email { get; set; } = "";

    public string Password { get; set; } = "";

    public bool Remember { get; set; }
}

// /login is where the route guard sends a visitor who is not signed in.
[AllowAnonymous]
[Route("/login")]
public sealed partial class LoginPage(IAuth auth) : AuthPage
{
    private readonly SignInModel _model = new();
    private AuthError _error;

    // Appended by the challenge redirect. Sanitised to a local URL before use, so a crafted link cannot turn
    // this page into an open redirect.
    [QueryParam]
    public string? ReturnUrl { get; set; }

    protected override Component? HeadAssets => Title["Sign in"];

    protected override Component? Content =>
        Fragment[
            H1.Class("text-2xl font-bold")["Sign in"],
            _error is AuthError.None ? null : Error("login-error", AuthMessages.For(_error)),
            Form.Model(_model).OnValidSubmit(SubmitAsync)[
                Field("email", "Email", Input.Bind(() => _model.Email).Id("email").Type(InputType.Email).Class("input w-full")),
                Field("password", "Password", Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Class("input w-full")),
                Label.Class("label cursor-pointer gap-2")[
                    Input.Bind(() => _model.Remember).Id("remember").Class("checkbox checkbox-sm"),
                    Span["Remember me"]
                ],
                Div.Class("card-actions mt-2")[
                    Button.Type("submit").Id("login-submit").Class("btn btn-primary btn-block")["Sign in"]
                ]
            ],
            P.Class("text-sm opacity-70")[
                "No account yet? ",
                NavLink.Href(Routes.RegisterPage()).Class("link link-primary")["Create one"],
                "."],
            P.Class("text-sm opacity-70")[
                NavLink.Href(Routes.ForgotPasswordPage()).Class("link link-primary")["Forgotten your password?"]]
        ];

    private async Task SubmitAsync(SignInModel model)
    {
        var result = await auth.SignInAsync(model.Email, model.Password, model.Remember, ReturnUrl);
        _error = result.Error;
    }
}
