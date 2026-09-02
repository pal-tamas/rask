namespace Rask.Core.Authentication;

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

    /// <summary>Too many failed attempts; the account is locked for a while.</summary>
    LockedOut = 2,

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
}

/// <summary>The outcome of a register or sign-in attempt.</summary>
/// <param name="Succeeded">Whether the attempt succeeded.</param>
/// <param name="Error">Why it did not, when it did not.</param>
/// <param name="Message">
/// A human-readable detail for the cases that carry one — a password-policy failure names what was
/// missing. <c>null</c> whenever <see cref="Error"/> alone says everything.
/// </param>
public sealed record AuthResult(bool Succeeded, AuthError Error = AuthError.None, string? Message = null)
{
    /// <summary>A successful attempt.</summary>
    public static AuthResult Success { get; } = new(true);

    /// <summary>A failed attempt.</summary>
    /// <param name="error">Why it failed.</param>
    /// <param name="message">An optional human-readable detail.</param>
    public static AuthResult Fail(AuthError error, string? message = null) => new(false, error, message);
}

/// <summary>
/// Register, sign in, sign out — the three flows, identical on every host.
/// </summary>
/// <remarks>
/// <para>
/// This is the one interface an app calls to move somebody between signed-out and signed-in. It is
/// injected the same way, and behaves the same way, whether the component runs on the Server host, in
/// WebAssembly, or inside an island — the implementations differ, the call site does not.
/// </para>
/// <para>
/// On the Server host it validates against the account store and then hands the principal to
/// <see cref="IAuthSignIn"/>, which runs the existing ticket relay (a WebSocket cannot write a
/// <c>Set-Cookie</c> itself). In the browser it posts to the app's own <c>/api/auth</c> endpoints and
/// refreshes <see cref="IUserProvider"/>. A TypeScript front end calls those same endpoints through the
/// <c>auth</c> module, with the same three verbs.
/// </para>
/// <para>
/// Reading <em>who</em> is signed in is <see cref="IUserProvider"/>'s job, not this one's.
/// </para>
/// <example>
/// <code>
/// public sealed class LoginForm(IAuth auth) : Component
/// {
///     private async Task SubmitAsync(Credentials c)
///     {
///         var result = await auth.SignInAsync(c.Email, c.Password, returnUrl: "/");
///         if (!result.Succeeded) { _error = result.Error; }
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public interface IAuth
{
    /// <summary>Creates an account and signs it in.</summary>
    /// <param name="email">The email address, which is also the user name.</param>
    /// <param name="password">The password, held only long enough to hash it.</param>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    /// <param name="firstRunToken">
    /// The first-run token, required only while no account exists yet. Ignored once the instance has
    /// been claimed.
    /// </param>
    Task<AuthResult> RegisterAsync(
        string email, string password, string? returnUrl = null, string? firstRunToken = null);

    /// <summary>Signs an existing account in.</summary>
    /// <param name="email">The email address.</param>
    /// <param name="password">The password.</param>
    /// <param name="remember">Whether the session should outlive the browser session.</param>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    Task<AuthResult> SignInAsync(
        string email, string password, bool remember = false, string? returnUrl = null);

    /// <summary>Signs the current visitor out.</summary>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    Task SignOutAsync(string? returnUrl = null);
}
