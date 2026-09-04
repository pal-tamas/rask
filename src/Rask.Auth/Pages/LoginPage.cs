using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Auth.Pages;

/// <summary>The credentials a visitor signs in with.</summary>
public sealed class SignInModel
{
    /// <summary>The email address the account was registered with.</summary>
    public string Email { get; set; } = "";

    /// <summary>The password.</summary>
    public string Password { get; set; } = "";
}

/// <summary>
/// The built-in sign-in page, at <c>/login</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>/login</c> is already <c>RouteAuthorizationGuard.ChallengePath</c>, so this is where the route
/// guard has always sent an unauthenticated visitor — until now it landed on whatever the app had
/// written there, or on nothing at all.
/// </para>
/// <para>
/// <b>An app replaces this by declaring its own page at the same route.</b> Nothing needs to be turned
/// off first: an app's own routes win over the framework's.
/// </para>
/// </remarks>
[AllowAnonymous]
[Route("login")]
public sealed partial class LoginPage(IAuth auth) : AuthPage
{
    private readonly SignInModel _model = new();
    private AuthError _error;

    /// <summary>Where to land after signing in.</summary>
    /// <remarks>
    /// The cookie middleware appends <c>?ReturnUrl=</c> to its challenge redirect, and
    /// <c>QueryParam</c> binds it case-insensitively. It is sanitised to a local URL before use, so a
    /// crafted link cannot turn this page into an open redirect.
    /// </remarks>
    [QueryParam]
    public string? ReturnUrl { get; set; }

    /// <inheritdoc />
    protected override Component? Content =>
        Fragment[
            H1["Sign in"],
            _error is AuthError.None ? null : Div.Class("rask-auth-error").Id("login-error")[AuthMessages.For(_error)],
            Form.Model(_model).OnValidSubmitAsync(SubmitAsync)[
                Field("email", "Email", Input.Bind(() => _model.Email).Id("email").Type(InputType.Email)),
                Field("password", "Password", Input.Bind(() => _model.Password).Id("password").Type(InputType.Password)),
                Button.Type("submit").Id("login-submit")["Sign in"]
            ],
            P.Class("rask-auth-note")["No account yet? ", NavLink.Href(Routes.RegisterPage())["Create one"], "."],
            P.Class("rask-auth-note")[
                NavLink.Href(Routes.ForgotPasswordPage())["Forgotten your password?"]]
        ];

    private async Task SubmitAsync(SignInModel model)
    {
        var result = await auth.SignInAsync(model.Email, model.Password, returnUrl: ReturnUrl);
        _error = result.Error;
    }
}
