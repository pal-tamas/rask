using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Auth.Pages;

/// <summary>
/// The built-in page an emailed confirmation link lands on, at <c>/confirm-email</c>.
/// </summary>
/// <remarks>
/// <para>
/// It confirms behind a BUTTON, not on arrival (#1013). The token is single-use, and a GET is not a
/// deliberate act by the person the link was sent to: mail scanners, link previewers, corporate
/// URL-rewriting gateways and prefetchers all fetch it first. Whichever of them arrives first spends the
/// token, and the human then clicks their own link and is told it did not work — with no way to tell
/// that from a real expiry, and a fresh link doing exactly the same thing.
/// </para>
/// <para>
/// The earlier reasoning — "the link was the deliberate act, a second click adds a step and no safety"
/// — is right about the person and wrong about who else fetches the URL. A confirmation is a state
/// change and belongs behind something a scanner will not do.
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
    private bool _busy;

    /// <summary>The account the link named.</summary>
    [QueryParam]
    public string? UserId { get; set; }

    /// <summary>The token the link carried.</summary>
    [QueryParam]
    public string? Token { get; set; }

    /// <inheritdoc />
    protected override Component? Content =>
        _confirmed ? Confirmed
        : _error is AuthError.EmailAlreadyConfirmed ? AlreadyConfirmed
        : _error is not AuthError.None ? Failed
        : _busy ? Working
        : Ready;

    // The state a scanner sees: a page, a button, and no state change. Nothing here spends the token.
    private Component Ready =>
        Fragment[
            H1["Confirm your email"],
            P["Press the button to finish confirming this address."],
            Button.Type("button").Id("confirm-submit").OnClickAsync(ConfirmAsync)["Confirm my email"]
        ];

    private Component AlreadyConfirmed =>
        Fragment[
            H1["Already confirmed"],
            Div.Class("rask-auth-ok").Id("confirm-already")[AuthMessages.For(_error)],
            P[NavLink.Href(Routes.LoginPage())["Sign in"], "."]
        ];

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

    // The frame between the click and the answer. Reachable now that confirming is a click rather than
    // part of the first render.
    private static Component Working => Fragment[H1["Confirming…"]];

    /// <summary>
    /// Confirms as soon as the link's parameters are known.
    /// </summary>
    /// <remarks>
    /// <c>OnPropsChangedAsync</c> rather than <c>OnMountAsync</c>: the two values arrive as query
    /// parameters, and this is the hook that fires once they are bound — on the first render, and again
    /// only if they actually change.
    /// </remarks>
    protected override Task OnPropsChangedAsync()
    {
        // Only the shape of the link is judged here. Confirming is what ConfirmAsync does, and it runs
        // from a click — never from arriving.
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
