using Microsoft.AspNetCore.Authorization;
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
public sealed partial class LoginPage(IAuth auth, IWebAuthn webAuthn) : AuthPage
{
    private readonly SignInModel _model = new();
    private AuthError _error;
    private bool _passkeysSupported;

    // Appended by the challenge redirect. Sanitised to a local URL before use, so a crafted link cannot turn
    // this page into an open redirect.
    [QueryParam]
    public string? ReturnUrl { get; set; }

    protected override string? PageTitle => "Sign in";

    // The support check is JavaScript, so it waits for a browser to exist: on the first render this page is HTML
    // on its way out, with nothing to ask.
    protected override async Task OnFirstRender()
    {
        _passkeysSupported = await webAuthn.IsSupported();
        StateHasChanged();
    }

    protected override Component? Content =>
        [
            Title("Sign in"),
            _error is AuthError.None ? null : Error("login-error", AuthMessages.For(_error)),
            Form.Model(_model).OnSubmit(Submit).Class("flex flex-col gap-6")[
                Ui.Input.Bind(() => _model.Email).Id("email").Type(InputType.Email).Label("Email"),
                Ui.Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Label("Password"),
                Ui.Checkbox.Bind(() => _model.Remember).Id("remember").Label("Remember me"),
                Ui.Button.Primary.Type(Ui.ButtonType.Submit).Id("login-submit").Class("w-full")["Sign in"]
            ],
            _passkeysSupported
                ? Div.Class("flex flex-col gap-6")[
                    Ui.Separator.Text("or"),
                    Ui.Button.Id("login-passkey").Class("w-full").OnClick(Passkey)["Sign in with a passkey"]
                ]
                : null,
            Div.Class("space-y-2 text-center")[
                Ui.Text["No account yet? ", Ui.Link.Href(Routes.RegisterPage())["Create one"], "."],
                Ui.Text[Ui.Link.Href(Routes.ForgotPasswordPage())["Forgotten your password?"]]
            ]
        ];

    private async Task Submit(SignInModel model)
    {
        var result = await auth.SignIn(model.Email, model.Password, model.Remember, ReturnUrl);
        _error = result.Error;
    }

    // No email and no password: the authenticator offers whichever accounts it holds for this site, and the one
    // the visitor picks is the one that signs in.
    private async Task Passkey()
    {
        var result = await auth.SignInWithPasskey(_model.Remember, ReturnUrl);
        _error = result.Error;
    }
}
