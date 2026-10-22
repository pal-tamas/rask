using Microsoft.AspNetCore.Authorization;
using Rask.Wire;

namespace Company.RaskServer.Features.Auth;

public sealed class ForgotPasswordModel
{
    public string Email { get; set; } = "";
}

// The answer is the same whether or not the address has an account here. Anything else would let anybody walk
// a list of addresses through this form to learn who is registered.
[AllowAnonymous]
[Route("/forgot-password")]
public sealed partial class ForgotPasswordPage(IAuth auth) : AuthPage
{
    private readonly ForgotPasswordModel _model = new();
    private AuthError _error;
    private bool _sent;

    protected override string? PageTitle => "Reset your password";

    protected override Component? Content => _sent ? Sent : Ask;

    private Component Sent =>
        [
            Title("Check your email"),
            Ok("forgot-sent", "If an account exists for that address, a link to choose a new password is on its way."),
            Ui.Text["The link works once, and expires. ", Ui.Link.Href(Routes.LoginPage())["Back to sign in"], "."]
        ];

    private Component Ask =>
        [
            Div.Class("space-y-2")[
                Title("Reset your password"),
                Ui.Text["Tell us the address you signed up with and we will send you a link."]
            ],
            _error is AuthError.None ? null : Error("forgot-error", AuthMessages.For(_error)),
            Form.Model(_model).OnSubmit(Submit).Class("flex flex-col gap-6")[
                Ui.Input.Bind(() => _model.Email).Id("email").Type(InputType.Email).Label("Email"),
                Ui.Button.Primary.Type(Ui.ButtonType.Submit).Id("forgot-submit").Class("w-full")["Send the link"]
            ],
            Ui.Text.Class("text-center")["Remembered it? ", Ui.Link.Href(Routes.LoginPage())["Sign in"], "."]
        ];

    private async Task Submit(ForgotPasswordModel model)
    {
        var result = await auth.SendPasswordReset(model.Email);
        _error = result.Error;
        _sent = result.Succeeded;
    }
}
