using Rask.Core.Authentication;

namespace Rask.Auth;

/// <summary>
/// <see cref="IAuth"/> for a component running on the Server host.
/// </summary>
/// <remarks>
/// The session is issued through <see cref="IAuthSignIn"/> rather than by writing a cookie here,
/// because a component handler runs on the WebSocket and a WebSocket cannot write a
/// <c>Set-Cookie</c>. <c>IAuthSignIn</c> records the intent, the host mints a single-use
/// session-bound ticket, the browser redeems it over ordinary HTTP, and the socket reconnects with the
/// new identity. That relay already exists and is already hardened; this type only decides <em>whether</em>
/// somebody may sign in.
/// </remarks>
/// <typeparam name="TUser">The application's user entity.</typeparam>
internal sealed class ServerAuth<TUser>(
    AccountService<TUser> accounts,
    IAuthSignIn signIn,
    AuthOptions options) : IAuth
    where TUser : RaskUser, new()
{
    public async Task<AuthResult> RegisterAsync(
        string email, string password, string? returnUrl = null, string? firstRunToken = null)
    {
        var outcome = await accounts.RegisterAsync(email, password, firstRunToken).ConfigureAwait(false);

        if (outcome is { Result.Succeeded: true, Principal: { } principal })
        {
            await signIn.SignInAsync(principal, returnUrl).ConfigureAwait(false);
        }

        return outcome.Result;
    }

    public async Task<AuthResult> SignInAsync(
        string email, string password, bool remember = false, string? returnUrl = null)
    {
        var outcome = await accounts.ValidateAsync(email, password).ConfigureAwait(false);

        if (outcome is { Result.Succeeded: true, Principal: { } principal })
        {
            await signIn.SignInAsync(principal, returnUrl).ConfigureAwait(false);
        }

        return outcome.Result;
    }

    public Task SignOutAsync(string? returnUrl = null) =>
        signIn.SignOutAsync(returnUrl ?? options.LoginPath);
}
