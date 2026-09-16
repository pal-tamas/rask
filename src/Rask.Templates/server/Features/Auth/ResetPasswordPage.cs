using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Wire;

namespace Company.RaskServer.Features.Auth;

public sealed class ResetPasswordModel
{
    public string Password { get; set; } = "";

    public string Confirm { get; set; } = "";
}

// Reached from an emailed link carrying userId and token. No session is issued on success: a reset link gets
// forwarded, and reading the email must not be enough to be signed in. Every session the account had ends.
[AllowAnonymous]
[Route("/reset-password")]
public sealed partial class ResetPasswordPage(IAuth auth) : AuthPage
{
    private readonly ResetPasswordModel _model = new();
    private AuthError _error;
    private string? _mismatch;
    private bool _done;

    [QueryParam]
    public string? UserId { get; set; }

    [QueryParam]
    public string? Token { get; set; }

    protected override Component? HeadAssets => Title["Choose a new password"];

    protected override Component? Content =>
        _done ? Done
        : string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token) ? Incomplete
        : Ask;

    private Component Done =>
        [
            H1.Class("text-2xl font-bold")["Password changed"],
            Ok("reset-done", "Your password has been changed, and every session for this account is signed out."),
            P.Class("text-sm opacity-70")[NavLink.Href(Routes.LoginPage()).Class("link link-primary")["Sign in"], "."]
        ];

    private Component Incomplete =>
        [
            H1.Class("text-2xl font-bold")["That link is incomplete"],
            Error("reset-error", "Open the link from your email in full, or ask for a new one."),
            P.Class("text-sm opacity-70")[
                NavLink.Href(Routes.ForgotPasswordPage()).Class("link link-primary")["Send another link"], "."]
        ];

    private Component Ask =>
        [
            H1.Class("text-2xl font-bold")["Choose a new password"],
            Message is null ? null : Error("reset-error", Message),
            Form.Model(_model).OnValidSubmit(SubmitAsync)[
                Field("password", "New password", Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Class("input w-full")),
                Field("confirm", "New password again", Input.Bind(() => _model.Confirm).Id("confirm").Type(InputType.Password).Class("input w-full")),
                Div.Class("card-actions mt-2")[
                    Button.Type("submit").Id("reset-submit").Class("btn btn-primary btn-block")["Change my password"]
                ]
            ]
        ];

    private string? Message => _mismatch ?? (_error is AuthError.None ? null : AuthMessages.For(_error));

    private async Task SubmitAsync(ResetPasswordModel model)
    {
        _mismatch = null;
        _error = AuthError.None;

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
