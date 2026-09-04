using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Auth.Pages;

/// <summary>What a visitor supplies to create an account.</summary>
public sealed class RegisterModel
{
    /// <summary>The email address, which is also the user name.</summary>
    public string Email { get; set; } = "";

    /// <summary>The password.</summary>
    public string Password { get; set; } = "";

    /// <summary>The first-run token, needed only while this app has no accounts yet.</summary>
    public string FirstRunToken { get; set; } = "";
}

/// <summary>
/// The built-in registration page, at <c>/register</c>.
/// </summary>
/// <remarks>
/// The first-run token field appears only while the app is unclaimed, because that is the only time it
/// is asked for. Showing it always would make every later registration look like it needed a secret;
/// hiding it while it is required would leave the first person with no way to enter one.
/// </remarks>
[AllowAnonymous]
[Route("register")]
public sealed partial class RegisterPage(IAuth auth, FirstRunToken firstRun) : AuthPage
{
    private readonly RegisterModel _model = new();
    private AuthError _error;
    private string? _detail;

    /// <summary>Where to land after registering. Sanitised to a local URL before use.</summary>
    [QueryParam]
    public string? ReturnUrl { get; set; }

    /// <inheritdoc />
    protected override Component? Content =>
        Fragment[
            H1[firstRun.IsPending ? "Claim this app" : "Create an account"],
            _error is AuthError.None
                ? null
                : Div.Class("rask-auth-error").Id("register-error")[_detail ?? AuthMessages.For(_error)],
            firstRun.IsPending
                ? P.Class("rask-auth-note").Id("register-first-run")[
                    "This app has no accounts yet, so this one becomes the administrator. "
                    + "The one-time token is in the startup log."]
                : null,
            Form.Model(_model).OnValidSubmitAsync(SubmitAsync)[
                Field("email", "Email", Input.Bind(() => _model.Email).Id("email").Type(InputType.Email)),
                Field("password", "Password", Input.Bind(() => _model.Password).Id("password").Type(InputType.Password)),
                firstRun.IsPending
                    ? Field("first-run-token", "First-run token", Input.Bind(() => _model.FirstRunToken).Id("first-run-token"))
                    : null,
                Button.Type("submit").Id("register-submit")[firstRun.IsPending ? "Claim it" : "Create account"]
            ],
            P.Class("rask-auth-note")["Already have an account? ", NavLink.Href(Routes.LoginPage())["Sign in"], "."]
        ];

    private async Task SubmitAsync(RegisterModel model)
    {
        var result = await auth.RegisterAsync(
            model.Email,
            model.Password,
            returnUrl: ReturnUrl,
            firstRunToken: string.IsNullOrWhiteSpace(model.FirstRunToken) ? null : model.FirstRunToken);

        _error = result.Error;

        // A policy failure carries the reason it was rejected — "must be at least 8 characters" is
        // actionable in a way the code alone is not.
        _detail = result.Message;
    }
}
