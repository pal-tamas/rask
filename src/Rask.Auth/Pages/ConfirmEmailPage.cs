using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Auth.Pages;

/// <summary>
/// The built-in page an emailed confirmation link lands on, at <c>/confirm-email</c>.
/// </summary>
/// <remarks>
/// <para>
/// It confirms on arrival rather than behind a button. The link was the deliberate act; asking somebody
/// who already clicked "confirm my email" in their inbox to click "confirm my email" again on the page
/// adds a step and no safety — the token is single-use either way.
/// </para>
/// <para>
/// <b>An app replaces this by declaring its own page at the same route.</b> Nothing needs to be turned
/// off first: an app's own routes win over the framework's.
/// </para>
/// </remarks>
[AllowAnonymous]
[Route("confirm-email")]
public sealed partial class ConfirmEmailPage(IAuth auth) : AuthPage
{
    private AuthError _error;
    private bool _confirmed;

    /// <summary>The account the link named.</summary>
    [QueryParam]
    public string? UserId { get; set; }

    /// <summary>The token the link carried.</summary>
    [QueryParam]
    public string? Token { get; set; }

    /// <inheritdoc />
    protected override Component? Content =>
        _confirmed ? Confirmed
        : _error is AuthError.None ? Working
        : Failed;

    private Component Confirmed =>
        Fragment[
            H1["Email confirmed"],
            Div.Class("rask-auth-ok").Id("confirm-ok")["Your email address is confirmed."],
            P[NavLink.Href(Routes.LoginPage())["Sign in"], "."]
        ];

    private Component Failed =>
        Fragment[
            H1["That link did not work"],
            Div.Class("rask-auth-error").Id("confirm-error")[AuthMessages.For(_error)],
            P[NavLink.Href(Routes.LoginPage())["Back to sign in"], "."]
        ];

    // Server-side this is never painted — the first response already carries the outcome, because
    // OnPropsChangedAsync is awaited before the page serializes. In the browser it is the frame between
    // arriving and the POST answering.
    private static Component Working => Fragment[H1["Confirming…"]];

    /// <summary>
    /// Confirms as soon as the link's parameters are known.
    /// </summary>
    /// <remarks>
    /// <c>OnPropsChangedAsync</c> rather than <c>OnMountAsync</c>: the two values arrive as query
    /// parameters, and this is the hook that fires once they are bound — on the first render, and again
    /// only if they actually change.
    /// </remarks>
    protected override async Task OnPropsChangedAsync()
    {
        if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token))
        {
            _error = AuthError.InvalidToken;
            return;
        }

        var result = await auth.ConfirmEmailAsync(UserId, Token);

        _error = result.Error;
        _confirmed = result.Succeeded;
    }
}
