using Rask.Core.Authentication;

namespace Rask.Auth.Pages;

/// <summary>
/// What the built-in pages say for each <see cref="AuthError" />.
/// </summary>
/// <remarks>
/// A class of its own rather than a static member on <c>LoginPage</c>, and not because of tidiness:
/// inside a markup host the bare name <c>LoginPage</c> is that component's chain entry
/// (<c>Build&lt;LoginPage&gt;</c>), not the type, so <c>LoginPage.Message(...)</c> does not compile from
/// another page. A non-component type has no entry and stays reachable by its own name.
/// </remarks>
internal static class AuthMessages
{
    /// <summary>What to show for a refusal.</summary>
    /// <remarks>
    /// <see cref="AuthError.InvalidCredentials" /> deliberately does not say which half was wrong —
    /// saying so would turn the sign-in page into an account-existence oracle. A lockout does say so,
    /// because somebody who cannot get in needs to know that waiting is the answer.
    /// </remarks>
    public static string For(AuthError error) => error switch
    {
        AuthError.LockedOut => "Too many attempts. This account is locked for a few minutes.",
        AuthError.NotAllowed => "This account is not allowed to sign in yet.",
        AuthError.DuplicateAccount => "An account with that email already exists.",
        AuthError.FirstRunTokenRequired =>
            "This app has no accounts yet, and that first-run token is not the right one. "
            + "It is written to the startup log.",
        AuthError.WeakPassword => "That password does not meet this app's policy.",
        AuthError.InvalidEmail => "That does not look like an email address this app accepts.",
        _ => "Wrong email or password.",
    };
}
