using Rask.Wire;

namespace Company.RaskServer.Features.Auth;

// What the pages say for each refusal. A class of its own because inside a markup host a page's bare name is
// its chain entry, not the type.
internal static class AuthMessages
{
    // InvalidCredentials never says which half was wrong: saying so would tell anybody which addresses have an
    // account here.
    public static string For(AuthError error) => error switch
    {
        AuthError.TooManyAttempts => "Too many attempts. Wait a minute and try again.",
        AuthError.NotAllowed => "This account is not allowed to sign in yet.",
        AuthError.DuplicateAccount => "An account with that email already exists.",
        AuthError.FirstRunTokenRequired =>
            "This app has no accounts yet, and that first-run token is not the right one. "
            + "It is written to the startup log.",
        AuthError.WeakPassword => "That password does not meet this app's policy.",
        AuthError.InvalidEmail => "That does not look like an email address.",
        AuthError.EmailNotConfirmed =>
            "Confirm your email address before signing in. The link was sent when you registered.",
        AuthError.EmailAlreadyConfirmed => "That address is already confirmed. You can sign in.",
        AuthError.InvalidToken => "That link has expired or has already been used. Ask for a new one.",
        AuthError.MailNotConfigured =>
            "This app cannot send email yet, so a reset link cannot be sent. "
            + "Configure the mail battery with a From address and an SMTP host.",
        _ => "Wrong email or password.",
    };
}
