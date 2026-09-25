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

    protected override Component? HeadAssets => Title["Confirm your email"];

    protected override Component? Content =>
        _confirmed ? Confirmed
        : _error is AuthError.EmailAlreadyConfirmed ? AlreadyConfirmed
        : _error is not AuthError.None ? Failed
        : _busy ? Working
        : Ready;

    private Component Ready =>
        [
            H1.Class("text-2xl font-bold")["Confirm your email"],
            P.Class("text-sm opacity-70")["Press the button to finish confirming this address."],
            Div.Class("card-actions mt-2")[
                Button.Type("button").Id("confirm-submit").Class("btn btn-primary btn-block").OnClick(ConfirmAsync)["Confirm my email"]
            ]
        ];

    private Component AlreadyConfirmed =>
        [
            H1.Class("text-2xl font-bold")["Already confirmed"],
            Ok("confirm-already", AuthMessages.For(_error)),
            P.Class("text-sm opacity-70")[NavLink.Href(Routes.LoginPage()).Class("link link-primary")["Sign in"], "."]
        ];

    private Component Confirmed =>
        [
            H1.Class("text-2xl font-bold")["Email confirmed"],
            Ok("confirm-ok", "Your email address is confirmed."),
            P.Class("text-sm opacity-70")[NavLink.Href(Routes.LoginPage()).Class("link link-primary")["Sign in"], "."]
        ];

    private Component Failed =>
        [
            H1.Class("text-2xl font-bold")["That link did not work"],
            Error("confirm-error", AuthMessages.For(_error)),
            P.Class("text-sm opacity-70")[NavLink.Href(Routes.LoginPage()).Class("link link-primary")["Back to sign in"], "."]
        ];

    private static Component Working =>
        [
            H1.Class("text-2xl font-bold")["Confirming…"],
            Span.Class("loading loading-spinner").Attributes(("aria-hidden", "true"))
        ];

    protected override Task OnUpdated()
    {
        if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token))
        {
            _error = AuthError.InvalidToken;
        }

        return Task.CompletedTask;
    }

    private async Task ConfirmAsync()
    {
        if (_busy || string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token))
        {
            return;
        }

        _busy = true;
        try
        {
            var result = await auth.ConfirmEmailAsync(UserId, Token);
            _error = result.Error;
            _confirmed = result.Succeeded;
        }
        finally
        {
            _busy = false;
        }
    }
}
