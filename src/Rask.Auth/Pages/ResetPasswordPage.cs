using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Auth.Pages;

/// <summary>The new password, twice.</summary>
public sealed class ResetPasswordModel
{
    /// <summary>The new password.</summary>
    public string Password { get; set; } = "";

    /// <summary>The same password again.</summary>
    public string Confirm { get; set; } = "";
}

/// <summary>
/// The built-in "choose a new password" page, at <c>/reset-password</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reached from an emailed link carrying <c>userId</c> and <c>token</c>. The token is what authorizes
/// the change — the visitor is signed out, and proving they can read that mailbox is the whole check.
/// </para>
/// <para>
/// No session is issued on success. A reset link lives in an inbox and gets forwarded; signing the
/// visitor in here would make reading the email enough to be signed in, on top of the password change.
/// </para>
/// </remarks>
[AllowAnonymous]
[Route("reset-password")]
public sealed partial class ResetPasswordPage(IAuth auth) : AuthPage
{
    private readonly ResetPasswordModel _model = new();
    private AuthError _error;
    private string? _mismatch;
    private bool _done;

    /// <summary>The account the link named.</summary>
    [QueryParam]
    public string? UserId { get; set; }

    /// <summary>The token the link carried.</summary>
    [QueryParam]
    public string? Token { get; set; }

    /// <inheritdoc />
    protected override Component? Content =>
        _done ? Done
        : string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token) ? Incomplete
        : Ask;

    private Component Done =>
        Fragment[
            H1["Password changed"],
            Div.Class("rask-auth-ok").Id("reset-done")[
                "Your password has been changed, and every other session for this account is signed out."],
            P[NavLink.Href(Routes.LoginPage())["Sign in"], "."]
        ];

    // The page a visitor lands on when they type the address by hand, or when a mail client mangles a
    // long link. Sent back to the start rather than shown a form that cannot possibly work.
    private Component Incomplete =>
        Fragment[
            H1["That link is incomplete"],
            Div.Class("rask-auth-error").Id("reset-error")[
                "Open the link from your email in full, or ask for a new one."],
            P[NavLink.Href(Routes.ForgotPasswordPage())["Send another link"], "."]
        ];

    private Component Ask =>
        Fragment[
            H1["Choose a new password"],
            Message is null ? null : Div.Class("rask-auth-error").Id("reset-error")[Message],
            Form.Model(_model).OnValidSubmitAsync(SubmitAsync)[
                Field(
                    "password",
                    "New password",
                    Input.Bind(() => _model.Password).Id("password").Type(InputType.Password)),
                Field(
                    "confirm",
                    "New password again",
                    Input.Bind(() => _model.Confirm).Id("confirm").Type(InputType.Password)),
                Button.Type("submit").Id("reset-submit")["Change my password"]
            ]
        ];

    private string? Message => _mismatch ?? (_error is AuthError.None ? null : AuthMessages.For(_error));

    private async Task SubmitAsync(ResetPasswordModel model)
    {
        _mismatch = null;
        _error = AuthError.None;

        // Checked here rather than by a validator, so the page works on an app that has neither
        // validation package installed — these pages ship inside a package and cannot assume one.
        if (!string.Equals(model.Password, model.Confirm, StringComparison.Ordinal))
        {
            _mismatch = "Those two passwords do not match.";
            return;
        }

        var result = await auth.ResetPasswordAsync(UserId!, Token!, model.Password);

        _error = result.Error;
        _done = result.Succeeded;
    }
}
