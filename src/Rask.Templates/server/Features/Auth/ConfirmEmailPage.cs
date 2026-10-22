using Microsoft.AspNetCore.Authorization;
using Rask.Wire;

namespace Company.RaskServer.Features.Auth;

// Confirms behind a button, never on arrival: mail scanners and link previewers fetch the link first, and a
// confirmation on GET would be spent by them before the person clicks.
[AllowAnonymous]
[Route("/confirm-email")]
public sealed partial class ConfirmEmailPage(IAuth auth) : AuthPage
{
    private AuthError _error;
    private bool _confirmed;
    private bool _busy;

    [QueryParam]
    public string? UserId { get; set; }

    [QueryParam]
    public string? Token { get; set; }

    protected override string? PageTitle => "Confirm your email";

    protected override Component? Content =>
        _confirmed ? Confirmed
        : _error is AuthError.EmailAlreadyConfirmed ? AlreadyConfirmed
        : _error is not AuthError.None ? Failed
        : _busy ? Working
        : Ready;

    private Component Ready =>
        [
            Title("Confirm your email"),
            Ui.Text["Press the button to finish confirming this address."],
            Ui.Button.Primary.Id("confirm-submit").Class("w-full").OnClick(Confirm)["Confirm my email"]
        ];

    private Component AlreadyConfirmed =>
        [
            Title("Already confirmed"),
            Ok("confirm-already", AuthMessages.For(_error)),
            Ui.Text[Ui.Link.Href(Routes.LoginPage())["Sign in"], "."]
        ];

    private Component Confirmed =>
        [
            Title("Email confirmed"),
            Ok("confirm-ok", "Your email address is confirmed."),
            Ui.Text[Ui.Link.Href(Routes.LoginPage())["Sign in"], "."]
        ];

    private Component Failed =>
        [
            Title("That link did not work"),
            Error("confirm-error", AuthMessages.For(_error)),
            Ui.Text[Ui.Link.Href(Routes.LoginPage())["Back to sign in"], "."]
        ];

    private static Component Working =>
        [
            Title("Confirming…"),
            Ui.Icon.Name(Ui.IconName.Loading).Class("text-zinc-500 dark:text-zinc-400")
        ];

    protected override Task OnUpdated()
    {
        if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token))
        {
            _error = AuthError.InvalidToken;
        }

        return Task.CompletedTask;
    }

    private async Task Confirm()
    {
        if (_busy || string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token))
        {
            return;
        }

        _busy = true;
        try
        {
            var result = await auth.ConfirmEmail(UserId, Token);
            _error = result.Error;
            _confirmed = result.Succeeded;
        }
        finally
        {
            _busy = false;
        }
    }
}
