using Microsoft.AspNetCore.Authorization;
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
[Guest]
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

    protected override string? PageTitle => "Choose a new password";

    protected override Component? Content =>
        _done ? Done
        : string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token) ? Incomplete
        : Ask;

    private Component Done =>
        [
            Title("Password changed"),
            Ok("reset-done", "Your password has been changed, and every session for this account is signed out."),
            Ui.Text[Ui.Link.Href(Routes.LoginPage())["Sign in"], "."]
        ];

    private Component Incomplete =>
        [
            Title("That link is incomplete"),
            Error("reset-error", "Open the link from your email in full, or ask for a new one."),
            Ui.Text[Ui.Link.Href(Routes.ForgotPasswordPage())["Send another link"], "."]
        ];

    private Component Ask =>
        [
            Title("Choose a new password"),
            Message is null ? null : Error("reset-error", Message),
            Form.Model(_model).OnSubmit(Submit).Class("flex flex-col gap-6")[
                Ui.Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Label("New password"),
                Ui.Input.Bind(() => _model.Confirm).Id("confirm").Type(InputType.Password).Label("New password again"),
                Ui.Button.Primary.Type(Ui.ButtonType.Submit).Id("reset-submit").Class("w-full")["Change my password"]
            ]
        ];

    private string? Message => _mismatch ?? (_error is AuthError.None ? null : AuthMessages.For(_error));

    private async Task Submit(ResetPasswordModel model)
    {
        _mismatch = null;
        _error = AuthError.None;

        if (!string.Equals(model.Password, model.Confirm, StringComparison.Ordinal))
        {
            _mismatch = "Those two passwords do not match.";
            return;
        }

        var result = await auth.ResetPassword(UserId!, Token!, model.Password);
        _error = result.Error;
        _done = result.Succeeded;
    }
}
