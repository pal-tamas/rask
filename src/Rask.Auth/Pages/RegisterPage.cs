using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Wire;

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
            H1.Class("text-2xl font-bold")[firstRun.IsPending ? "Claim this app" : "Create an account"],
            _error is AuthError.None
                ? null
                : Error("register-error", _detail ?? AuthMessages.For(_error)),
            firstRun.IsPending
                ? Div.Class("alert alert-info").Role("status").Id("register-first-run")[
                    Span[
                        "This app has no accounts yet, so this one becomes the administrator. "
                        + "The one-time token is in the startup log."]]
                : null,
            Form.Model(_model).OnValidSubmit(SubmitAsync)[
                Field("email", "Email", Input.Bind(() => _model.Email).Id("email").Type(InputType.Email).Class("input w-full")),
                Field("password", "Password", Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Class("input w-full")),
                firstRun.IsPending
                    ? Field("first-run-token", "First-run token", Input.Bind(() => _model.FirstRunToken).Id("first-run-token").Class("input w-full"))
                    : null,
                Div.Class("card-actions mt-2")[
                    Button
                        .Type("submit")
                        .Id("register-submit")
                        .Class("btn btn-primary btn-block")[firstRun.IsPending ? "Claim it" : "Create account"]
                ]
            ],
            P.Class("text-sm opacity-70")[
                "Already have an account? ",
                NavLink.Href(Routes.LoginPage()).Class("link link-primary")["Sign in"],
                "."]
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
