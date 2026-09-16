using Microsoft.AspNetCore.Http;
using Rask.Core.Authentication;
using Rask.Core.Browser;
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
    IWebAuthn webAuthn,
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

    public async Task<AuthResult> AddPasskeyAsync(string? name = null)
    {
        if (AuthPrincipal.UserId(users.Current) is not { } userId)
        {
            return AuthResult.Fail(AuthError.NotAllowed);
        }

        // Begin and complete in one handler, so the challenge stays in this process and the sealed state never has to
        // travel: on this host the browser half is a call over the live socket rather than a second HTTP request.
        if (await accounts.BeginAddPasskeyAsync(userId, Origin()).ConfigureAwait(false) is not { } challenge)
        {
            return AuthResult.Fail(AuthError.NotAllowed);
        }

        var created = await webAuthn
            .CreateAsync(new PublicKeyCredentialCreationOptions
            {
                Challenge = challenge.Challenge,
                Rp = new RelyingParty(challenge.RelyingPartyName, challenge.RelyingPartyId),
                User = new PublicKeyCredentialUser(challenge.UserId, challenge.UserName, challenge.UserDisplayName),
                PubKeyCredParams = [new PubKeyCredParam(-7), new PubKeyCredParam(-257)],
                TimeoutMs = challenge.TimeoutMs,
                Attestation = "none",
                AuthenticatorSelection = new AuthenticatorSelection
                {
                    // Discoverable and verified, always: a passkey that cannot be found without an email is not what
                    // "sign in with a passkey" promises, and one that skips the biometric is a single factor.
                    ResidentKey = "required",
                    UserVerification = "required",
                },
                ExcludeCredentials = [.. challenge.ExcludeCredentials.Select(id => new CredentialDescriptor(id))],
            })
            .ConfigureAwait(false);

        if (created is null)
        {
            // The visitor dismissed the dialog, or it timed out. Not an error to throw at them.
            return AuthResult.Fail(AuthError.PasskeyRejected, "No passkey was created.");
        }

        return await accounts
            .CompleteAddPasskeyAsync(
                userId,
                new PasskeyRegistrationRequest(
                    challenge.State,
                    name,
                    created.RawId,
                    created.ClientDataJson,
                    created.AttestationObject,
                    created.Transports),
                Origin())
            .ConfigureAwait(false);
    }

    public async Task<AuthResult> SignInWithPasskeyAsync(bool remember = false, string? returnUrl = null)
    {
        if (accounts.BeginPasskeySignIn(Origin()) is not { } challenge)
        {
            return AuthResult.Fail(AuthError.NotAllowed);
        }

        var assertion = await webAuthn
            .GetAsync(new PublicKeyCredentialRequestOptions
            {
                Challenge = challenge.Challenge,
                RpId = challenge.RelyingPartyId,
                TimeoutMs = challenge.TimeoutMs,

                // No allow-list: the authenticator offers what it holds for this site, so the visitor types nothing.
                UserVerification = "required",
            })
            .ConfigureAwait(false);

        if (assertion is null)
        {
            return AuthResult.Fail(AuthError.InvalidCredentials);
        }

        var outcome = await accounts
            .CompletePasskeySignInAsync(
                new PasskeyLoginRequest(
                    challenge.State,
                    assertion.RawId,
                    assertion.ClientDataJson,
                    assertion.AuthenticatorData,
                    assertion.Signature,
                    assertion.UserHandle,
                    remember),
                Origin(),
                Client())
            .ConfigureAwait(false);

        if (outcome is { Result.Succeeded: true, Principal: { } principal })
        {
            await signIn.SignInAsync(principal, returnUrl, persistent: remember).ConfigureAwait(false);
        }

        return outcome.Result;
    }

    public Task<AuthResult> RemovePasskeyAsync(Guid id) =>
        AuthPrincipal.UserId(users.Current) is { } userId
            ? accounts.RemovePasskeyAsync(userId, id)
            : Task.FromResult(AuthResult.Fail(AuthError.NotAllowed));

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

    // Only consulted when the app configured neither PasskeyOrigins nor PublicOrigin, which is the development case.
    private string? Origin() =>
        http.HttpContext is { Request: { } request } ? request.Scheme + "://" + request.Host.Value : null;
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
