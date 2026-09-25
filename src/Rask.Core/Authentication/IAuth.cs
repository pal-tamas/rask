using Rask.Wire;

namespace Rask.Core.Authentication;

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

    /// <summary>Creates an account, sets the app's own columns on it, and signs it in.</summary>
    /// <typeparam name="TUser">The app's user type.</typeparam>
    /// <param name="email">The email address.</param>
    /// <param name="password">The password, held only long enough to hash it.</param>
    /// <param name="apply">Sets values on the new user before it is saved: <c>(User u) =&gt; u.Rename(name)</c>.</param>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    /// <param name="firstRunToken">The first-run token, required only while no account exists yet.</param>
    /// <exception cref="NotSupportedException">The host cannot run code on the new user, such as a browser client.</exception>
    Task<AuthResult> RegisterAsync<TUser>(
        string email,
        string password,
        Action<TUser> apply,
        string? returnUrl = null,
        string? firstRunToken = null)
        where TUser : class =>
        throw new NotSupportedException(
            "This host cannot set values on the new user while registering. Register, then change the user with "
            + "User.Update once the user is signed in.");

    /// <summary>Ends every other session of the signed-in user, leaving this one signed in.</summary>
    Task SignOutOtherDevicesAsync();

    /// <summary>Ends every session of the signed-in user, this one included.</summary>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    Task SignOutEverywhereAsync(string? returnUrl = null);

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

    /// <summary>Emails a password-reset link to <paramref name="email" />, if an account has it.</summary>
    /// <remarks>
    /// <b>Succeeds whether or not the account exists.</b> Answering differently would turn this into a
    /// register of who has an account here, which is exactly what an attacker with a list of addresses
    /// wants. The page says "if that address has an account, a link is on its way" either way.
    /// </remarks>
    Task<AuthResult> SendPasswordResetAsync(string email);

    /// <summary>Sets a new password using a token from a reset email.</summary>
    /// <param name="userId">The account id the link carried.</param>
    /// <param name="token">The reset token the link carried.</param>
    /// <param name="password">The new password.</param>
    Task<AuthResult> ResetPasswordAsync(string userId, string token, string password);

    /// <summary>Confirms an email address using a token from a confirmation email.</summary>
    /// <param name="userId">The account id the link carried.</param>
    /// <param name="token">The confirmation token the link carried.</param>
    Task<AuthResult> ConfirmEmailAsync(string userId, string token);

    /// <summary>
    /// Adds a passkey to the signed-in account: Touch ID, Windows Hello, a phone, or a security key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs the whole ceremony — the browser prompts, the authenticator signs, the server verifies and stores the
    /// public key. <b>Call it from a click handler</b>: browsers only show the passkey dialog for a real gesture, and
    /// a visitor who dismisses it gets <see cref="AuthError.PasskeyRejected" /> rather than an exception.
    /// </para>
    /// <para>
    /// A passkey is an extra way in, never a replacement: the account keeps its password, and
    /// <see cref="RemovePasskeyAsync" /> takes one away again.
    /// </para>
    /// <example>
    /// <code>
    /// Button.OnClick(() => auth.AddPasskeyAsync("MacBook"))["Add a passkey"]
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="name">What to call it in the account's device list. Defaults to "Passkey".</param>
    /// <exception cref="NotSupportedException">The host cannot run a passkey ceremony.</exception>
    Task<AuthResult> AddPasskeyAsync(string? name = null) =>
        throw new NotSupportedException(
            "This host cannot run a passkey ceremony. Passkeys need a browser: use the Server or WebAssembly host.");

    /// <summary>Signs in with a passkey, with no email and no password typed.</summary>
    /// <remarks>
    /// Discoverable: the authenticator offers whichever accounts it holds for this site, so nothing identifies the
    /// visitor beforehand. <b>Call it from a click handler.</b> The session it starts is the same session a password
    /// sign-in starts, and shows up on the device list beside the others.
    /// </remarks>
    /// <param name="remember">Whether the session should outlive the browser session.</param>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    /// <exception cref="NotSupportedException">The host cannot run a passkey ceremony.</exception>
    Task<AuthResult> SignInWithPasskeyAsync(bool remember = false, string? returnUrl = null) =>
        throw new NotSupportedException(
            "This host cannot run a passkey ceremony. Passkeys need a browser: use the Server or WebAssembly host.");

    /// <summary>Removes one of the signed-in account's passkeys. It stops signing anybody in at once.</summary>
    /// <param name="id">The passkey's id.</param>
    /// <exception cref="NotSupportedException">The host cannot run a passkey ceremony.</exception>
    Task<AuthResult> RemovePasskeyAsync(Guid id) =>
        throw new NotSupportedException(
            "This host cannot run a passkey ceremony. Passkeys need a browser: use the Server or WebAssembly host.");
}
