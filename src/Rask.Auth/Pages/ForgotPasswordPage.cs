using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Auth.Pages;

/// <summary>The address a reset link is sent to.</summary>
public sealed class ForgotPasswordModel
{
    /// <summary>The email address the account was registered with.</summary>
    public string Email { get; set; } = "";
}

/// <summary>
/// The built-in "email me a reset link" page, at <c>/forgot-password</c>.
/// </summary>
/// <remarks>
/// <para>
/// The answer is the same whether or not the address has an account here. Anything else — a different
/// message, a different delay, a different status — turns this form into a membership oracle that
/// anybody can walk a list of addresses through.
/// </para>
/// <para>
/// <b>An app replaces this by declaring its own page at the same route.</b> Nothing needs to be turned
/// off first: an app's own routes win over the framework's.
/// </para>
/// </remarks>
[AllowAnonymous]
[Route("forgot-password")]
public sealed partial class ForgotPasswordPage(IAuth auth) : AuthPage
{
    private readonly ForgotPasswordModel _model = new();
    private AuthError _error;
    private bool _sent;

    /// <inheritdoc />
    protected override Component? Content =>
        _sent ? Sent : Ask;

    private Component Sent =>
        Fragment[
            H1["Check your email"],
            Div.Class("rask-auth-ok").Id("forgot-sent")[
                "If an account exists for that address, a link to choose a new password is on its way."],
            P.Class("rask-auth-note")[
                "The link works once, and expires. ",
                NavLink.Href(Routes.LoginPage())["Back to sign in"], "."]
        ];

    private Component Ask =>
        Fragment[
            H1["Reset your password"],
            _error is AuthError.None
                ? null
                : Div.Class("rask-auth-error").Id("forgot-error")[AuthMessages.For(_error)],
            P["Tell us the address you signed up with and we will send you a link."],
            Form.Model(_model).OnValidSubmitAsync(SubmitAsync)[
                Field("email", "Email", Input.Bind(() => _model.Email).Id("email").Type(InputType.Email)),
                Button.Type("submit").Id("forgot-submit")["Send the link"]
            ],
            P.Class("rask-auth-note")[
                "Remembered it? ", NavLink.Href(Routes.LoginPage())["Sign in"], "."]
        ];

    private async Task SubmitAsync(ForgotPasswordModel model)
    {
        var result = await auth.SendPasswordResetAsync(model.Email);

        // The one failure worth showing is that the app cannot send email at all, which is an operator's
        // problem rather than the visitor's. Every other outcome reports as sent.
        _error = result.Error;
        _sent = result.Succeeded;
    }
}
