using Microsoft.AspNetCore.Http;
using Rask.Core.Authentication;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>
/// <see cref="IAuth"/> for a component running on the Server host.
/// </summary>
/// <remarks>
/// The session is issued through <see cref="IAuthSignIn"/> rather than by writing a cookie here, because a component
/// handler runs on the WebSocket and a WebSocket cannot write a <c>Set-Cookie</c>. <c>IAuthSignIn</c> records the intent,
/// the host mints a single-use session-bound ticket, the browser redeems it over ordinary HTTP — where the cookie handler
/// starts the <see cref="Session" /> row — and the socket reconnects with the new identity. This type only decides
/// <em>whether</em> somebody may sign in.
/// </remarks>
/// <typeparam name="TUser">The application's user aggregate.</typeparam>
internal sealed class ServerAuth<TUser>(
    AccountService<TUser> accounts,
    IAuthSignIn signIn,
    IUserProvider users,
    IHttpContextAccessor http,
    AuthOptions options) : IAuth
    where TUser : Authenticatable, new()
{
    public Task<AuthResult> RegisterAsync(
        string email, string password, string? returnUrl = null, string? firstRunToken = null) =>
        RegisterCoreAsync(email, password, apply: null, returnUrl, firstRunToken);

    public Task<AuthResult> RegisterAsync<T>(
        string email,
        string password,
        Action<T> apply,
        string? returnUrl = null,
        string? firstRunToken = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(apply);

        if (!typeof(T).IsAssignableFrom(typeof(TUser)))
        {
            throw new ArgumentException(
                $"The registering user is a {typeof(TUser).Name}, which is not a {typeof(T).Name}.", nameof(apply));
        }

        return RegisterCoreAsync(email, password, user => apply((T)(object)user), returnUrl, firstRunToken);
    }

    public async Task<AuthResult> SignInAsync(
        string email, string password, bool remember = false, string? returnUrl = null)
    {
        var outcome = await accounts.ValidateAsync(email, password, Client()).ConfigureAwait(false);

        if (outcome is { Result.Succeeded: true, Principal: { } principal })
        {
            await signIn.SignInAsync(principal, returnUrl, persistent: remember).ConfigureAwait(false);
        }

        return outcome.Result;
    }

    public Task SignOutAsync(string? returnUrl = null) =>
        signIn.SignOutAsync(returnUrl ?? options.LoginPath);

    public Task SignOutOtherDevicesAsync() => accounts.SignOutOtherDevicesAsync(users.Current);

    public async Task SignOutEverywhereAsync(string? returnUrl = null)
    {
        await accounts.SignOutEverywhereAsync(users.Current).ConfigureAwait(false);
        await signIn.SignOutAsync(returnUrl ?? options.LoginPath).ConfigureAwait(false);
    }

    public Task<AuthResult> SendPasswordResetAsync(string email) =>
        accounts.SendPasswordResetAsync(email, Client());

    public Task<AuthResult> ResetPasswordAsync(string userId, string token, string password) =>
        accounts.ResetPasswordAsync(userId, token, password);

    public Task<AuthResult> ConfirmEmailAsync(string userId, string token) =>
        accounts.ConfirmEmailAsync(userId, token);

    private async Task<AuthResult> RegisterCoreAsync(
        string email, string password, Action<TUser>? apply, string? returnUrl, string? firstRunToken)
    {
        var outcome = await accounts
            .RegisterAsync(email, password, firstRunToken, Client(), apply)
            .ConfigureAwait(false);

        if (outcome is { Result.Succeeded: true, Principal: { } principal })
        {
            await signIn.SignInAsync(principal, returnUrl).ConfigureAwait(false);
        }

        return outcome.Result;
    }

    // The socket's own request, when the host flows it to handlers; otherwise the throttle keys on the address alone.
    private string? Client() => http.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Checks a live session's principal against its <see cref="Session" /> row.</summary>
internal sealed class SessionRevalidator(IAuthSessions sessions) : ISessionRevalidator
{
    public async ValueTask<System.Security.Claims.ClaimsPrincipal?> RevalidateAsync(
        System.Security.Claims.ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return AuthPrincipal.SessionId(principal) is { } sessionId
            ? await sessions.ResumeAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : null;
    }
}
