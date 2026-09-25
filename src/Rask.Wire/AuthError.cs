namespace Rask.Wire;

/// <summary>Why an authentication attempt did not succeed.</summary>
/// <remarks>
/// A code rather than a message, so the page that renders it can say it in the visitor's own language.
/// </remarks>
public enum AuthError
{
    /// <summary>The attempt succeeded.</summary>
    None = 0,

    /// <summary>The email or the password was wrong. Deliberately does not say which.</summary>
    InvalidCredentials = 1,

    /// <summary>Too many attempts from this client in the last minute. The account is not locked; wait and try again.</summary>
    TooManyAttempts = 2,

    /// <summary>An account with that email already exists.</summary>
    DuplicateAccount = 3,

    /// <summary>The password did not meet the app's policy.</summary>
    WeakPassword = 4,

    /// <summary>
    /// This app has no accounts yet, and the first-run token presented was missing or wrong.
    /// </summary>
    /// <remarks>
    /// Missing and wrong are one code on purpose, carrying no detail: a caller learns only that it did
    /// not have the token, never how close it came. The comparison behind it is fixed-time for the same
    /// reason. Once an account exists the token stops being consulted at all, so this cannot be returned
    /// for a claimed instance.
    /// </remarks>
    FirstRunTokenRequired = 5,

    /// <summary>The account exists but is not permitted to sign in — unconfirmed, or disabled.</summary>
    NotAllowed = 6,

    /// <summary>The email address was not one the app will accept.</summary>
    InvalidEmail = 7,

    /// <summary>
    /// The link was not valid: wrong, already used, or past its lifetime.
    /// </summary>
    /// <remarks>
    /// One code for all three, carrying no detail. Which of them it was is not something the person
    /// holding the link can act on differently — they need a new one either way — and separating them
    /// tells anybody who guesses at links which guesses were closer.
    /// </remarks>
    InvalidToken = 8,

    /// <summary>
    /// The account exists but has not confirmed its email, and this app requires that before sign-in.
    /// </summary>
    EmailNotConfirmed = 9,

    /// <summary>
    /// The address is already confirmed, so the link had nothing left to do.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="InvalidToken" /> because it is the one case where "that link did not
    /// work" is actively misleading: everything the visitor wanted has already happened, and telling
    /// them to request a new link sends them round a loop that cannot end. It leaks nothing a holder of
    /// the link does not already know — they were sent it.
    /// </remarks>
    EmailAlreadyConfirmed = 11,

    /// <summary>
    /// The app cannot send email, so a flow that depends on one cannot start.
    /// </summary>
    /// <remarks>
    /// Reported rather than swallowed: a password reset that silently sends nothing looks identical to
    /// one that worked, and the person waiting for the email has no way to tell.
    /// </remarks>
    MailNotConfigured = 10,

    /// <summary>
    /// A passkey ceremony did not verify, or the passkey is not one this account can use.
    /// </summary>
    /// <remarks>
    /// Only ever returned to somebody already signed in — adding or removing a passkey — so it may carry a message
    /// saying which it was. A passkey <em>sign-in</em> that fails answers <see cref="InvalidCredentials" /> like a
    /// wrong password, because anyone can reach that endpoint.
    /// </remarks>
    PasskeyRejected = 12,
}
