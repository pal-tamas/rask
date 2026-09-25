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

    protected override Component? HeadAssets => Title["Sign in"];

    // The support check is JavaScript, so it waits for a browser to exist: on the first render this page is HTML
    // on its way out, with nothing to ask.
    protected override async Task OnFirstRendered()
    {
        _passkeysSupported = await webAuthn.IsSupportedAsync();
        StateHasChanged();
    }

    protected override Component? Content =>
        [
            H1.Class("text-2xl font-bold")["Sign in"],
            _error is AuthError.None ? null : Error("login-error", AuthMessages.For(_error)),
            Form.Model(_model).OnSubmit(SubmitAsync)[
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
            _passkeysSupported
                ? Div[
                    Div.Class("divider")["or"],
                    Button.Type("button").Id("login-passkey").Class("btn btn-outline btn-block").OnClick(PasskeyAsync)[
                        "Sign in with a passkey"]
                ]
                : null,
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

    // No email and no password: the authenticator offers whichever accounts it holds for this site, and the one
    // the visitor picks is the one that signs in.
    private async Task PasskeyAsync()
    {
        var result = await auth.SignInWithPasskeyAsync(_model.Remember, ReturnUrl);
        _error = result.Error;
    }
}
