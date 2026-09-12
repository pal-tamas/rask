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
}
