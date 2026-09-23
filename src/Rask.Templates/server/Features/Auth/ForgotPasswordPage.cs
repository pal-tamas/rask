using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;
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

    protected override Component? HeadAssets => Title["Reset your password"];

    protected override Component? Content => _sent ? Sent : Ask;

    private Component Sent =>
        [
            H1.Class("text-2xl font-bold")["Check your email"],
            Ok("forgot-sent", "If an account exists for that address, a link to choose a new password is on its way."),
            P.Class("text-sm opacity-70")[
                "The link works once, and expires. ",
                NavLink.Href(Routes.LoginPage()).Class("link link-primary")["Back to sign in"], "."]
        ];

    private Component Ask =>
        [
            H1.Class("text-2xl font-bold")["Reset your password"],
            _error is AuthError.None ? null : Error("forgot-error", AuthMessages.For(_error)),
            P.Class("text-sm opacity-70")["Tell us the address you signed up with and we will send you a link."],
            Form.Model(_model).OnSubmit(SubmitAsync)[
                Field("email", "Email", Input.Bind(() => _model.Email).Id("email").Type(InputType.Email).Class("input w-full")),
                Div.Class("card-actions mt-2")[
                    Button.Type("submit").Id("forgot-submit").Class("btn btn-primary btn-block")["Send the link"]
                ]
            ],
            P.Class("text-sm opacity-70")[
                "Remembered it? ", NavLink.Href(Routes.LoginPage()).Class("link link-primary")["Sign in"], "."]
        ];

    private async Task SubmitAsync(ForgotPasswordModel model)
    {
        var result = await auth.SendPasswordResetAsync(model.Email);
        _error = result.Error;
        _sent = result.Succeeded;
    }
}
