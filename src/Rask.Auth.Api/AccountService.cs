using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>What an account operation produced: the outcome, and the principal when it succeeded.</summary>
internal sealed record AccountOutcome(AuthResult Result, ClaimsPrincipal? Principal);

/// <summary>
/// The account store, without its user type.
/// </summary>
/// <remarks>
/// The endpoints are mapped by <c>MapRaskAuth()</c>, which has no way to know which account type an app declared — it is a
/// parameterless extension method on the endpoint builder, chosen so an app writes one line. Registering this alongside
/// the generic service gives the endpoints something to resolve that does not name the type.
/// </remarks>
internal interface IAccounts
{
    Task<AccountOutcome> RegisterAsync(
        string email,
        string password,
        string? firstRunToken,
        string? client,
        CancellationToken cancellationToken = default);

    Task<AccountOutcome> ValidateAsync(
        string email, string password, string? client, CancellationToken cancellationToken = default);

    Task<AuthResult> SendPasswordResetAsync(
        string email, string? client, CancellationToken cancellationToken = default);

    Task<AuthResult> ResetPasswordAsync(
        string userId, string token, string password, CancellationToken cancellationToken = default);

    Task<AuthResult> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default);

    Task<int> SignOutOtherDevicesAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);

    Task<int> SignOutEverywhereAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);

    Task<PasskeyCreationChallenge?> BeginAddPasskeyAsync(
        Guid userId, string? origin, CancellationToken cancellationToken = default);

    Task<AuthResult> CompleteAddPasskeyAsync(
        Guid userId, PasskeyRegistrationRequest request, string? origin, CancellationToken cancellationToken = default);

    PasskeyRequestChallenge? BeginPasskeySignIn(string? origin);

    Task<AccountOutcome> CompletePasskeySignInAsync(
        PasskeyLoginRequest request, string? origin, string? client, CancellationToken cancellationToken = default);

    Task<AuthResult> RemovePasskeyAsync(Guid userId, Guid passkeyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Register, check a password, reset and confirm — against the app's own <typeparamref name="TUser" /> aggregate.
/// </summary>
/// <remarks>
/// <para>
/// It stops at producing a <see cref="ClaimsPrincipal"/> and never issues a session, because the two callers issue one
/// differently and both are correct: a component handler on the Server host has no <c>HttpContext</c> to write a cookie on
/// and goes through the host's <c>IAuthSignIn</c> ticket relay, while the <c>/api/auth</c> endpoints call
/// <c>HttpContext.SignInAsync</c> directly. Both end on the cookie handler, which is where the <see cref="Session" /> row is
/// started (<see cref="AuthCookieEvents" />).
/// </para>
/// <para>
/// No answer here tells a caller whether an address has an account: an unknown address costs a password check anyway, a
/// reset request answers the same for every address, and "confirm your email" is only said after the right password.
/// </para>
/// </remarks>
/// <typeparam name="TUser">The application's user aggregate.</typeparam>
internal sealed class AccountService<TUser>(
    IAuthContexts contexts,
    IInstanceClaimStore claims,
    FirstRunToken firstRun,
    AuthMail mail,
    AuthOptions options,
    PasswordHasher hasher,
    AuthTokens tokens,
    AuthThrottle throttle,
    PasskeyChallenges challenges,
    PasskeySite site,
    IAuthSessions sessions,
    TimeProvider clock,
    ILogger<AccountService<TUser>> logger) : IAccounts
    where TUser : Authenticatable, new()
{
    private const string SignInPurpose = "sign-in";
    private const string RegisterPurpose = "register";
    private const string ResetPurpose = "reset";
    private const string PasskeyThrottlePurpose = "passkey";

    public Task<AccountOutcome> RegisterAsync(
        string email,
        string password,
        string? firstRunToken,
        string? client,
        CancellationToken cancellationToken = default) =>
        RegisterAsync(email, password, firstRunToken, client, apply: null, cancellationToken);

    /// <summary>Registers, applying <paramref name="apply" /> to the new user before it is saved.</summary>
    public async Task<AccountOutcome> RegisterAsync(
        string email,
        string password,
        string? firstRunToken,
        string? client,
        Action<TUser>? apply,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);

        var throttleKey = AuthThrottle.Key(RegisterPurpose, "", client);
        if (throttle.IsThrottled(throttleKey))
        {
            return Fail(AuthError.TooManyAttempts);
        }

        // The gate applies only while the instance is unclaimed. Asking the database rather than the in-memory token means
        // a restart cannot re-open the window on an app that already has accounts.
        if (options.FirstUserIsAdmin
            && options.RequireFirstRunToken
            && !await claims.IsClaimedAsync(cancellationToken).ConfigureAwait(false)
            && !firstRun.Matches(firstRunToken))
        {
            throttle.Hit(throttleKey);
            return Fail(AuthError.FirstRunTokenRequired);
        }

        if (!IsEmail(email))
        {
            return Fail(AuthError.InvalidEmail);
        }

        if (PasswordProblem(password) is { } problem)
        {
            return new AccountOutcome(AuthResult.Fail(AuthError.WeakPassword, problem), null);
        }

        var normalized = Authenticatable.NormalizeEmail(email);
        var now = clock.GetUtcNow().UtcDateTime;

        var user = new TUser();
        user.Register(normalized, hasher.Hash(password), now);
        apply?.Invoke(user);

        await using (var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // A deleted user still holds its address, so it cannot be registered again under someone else.
            if (await db.Set<TUser>()
                    .IgnoreQueryFilters()
                    .AnyAsync(u => u.Email == normalized, cancellationToken)
                    .ConfigureAwait(false))
            {
                throttle.Hit(throttleKey);
                return Fail(AuthError.DuplicateAccount);
            }

            db.Add(user);

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Two registrations for one address arrived together and the unique index kept one.
                return Fail(AuthError.DuplicateAccount);
            }
        }

        // Winning the claim is what makes this account the administrator, and only one caller can win it — the row's primary
        // key is a constant. A loser is not an error: it is the second person to register, and they get the ordinary role.
        var isAdmin = options.FirstUserIsAdmin
                      && await claims.TryClaimAsync(user.Id, cancellationToken).ConfigureAwait(false);

        await using (var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            db.Attach(user);
            user.GrantRole(isAdmin ? RaskRoles.Admin : RaskRoles.User);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (isAdmin)
        {
            firstRun.Clear();
        }

        // Sent whether or not RequireConfirmedEmail is on. With the gate off it is an invitation rather than a barrier — and an
        // app that turns the gate on later finds its existing accounts already confirmed, instead of locking all of them out.
        await mail
            .SendConfirmationAsync(
                user.Email, user.Id.ToString(), tokens.ForConfirmation(user, options.TokenLifetime), cancellationToken)
            .ConfigureAwait(false);

        return new AccountOutcome(AuthResult.Success, AuthPrincipal.For(user));
    }

    public async Task<AccountOutcome> ValidateAsync(
        string email, string password, string? client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);

        var throttleKey = AuthThrottle.Key(SignInPurpose, email, client);
        if (throttle.IsThrottled(throttleKey))
        {
            return Fail(AuthError.TooManyAttempts);
        }

        var normalized = Authenticatable.NormalizeEmail(email);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // TWO, not one. An address is unique WITHIN a tenant, so with tenants in play the same address can
        // belong to two accounts — and the accounts table is deliberately not filtered by tenant, because
        // sign-in has to find a user BEFORE it can know which tenant they are in. FirstOrDefault over an
        // unordered query would then sign somebody into whichever row the database happened to return first,
        // which is both non-deterministic and the wrong tenant half the time.
        var candidates = await db.Set<TUser>()
            .Where(u => u.Email == normalized)
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count > 1)
        {
            // Refused rather than resolved arbitrarily. Reported at Error because it is a configuration
            // problem the operator has to fix — an app with per-tenant addresses needs to say which tenant a
            // sign-in is for — and answered as ordinary invalid credentials so the response says nothing
            // about which addresses exist.
            logger.LogError(
                "Sign-in for an address held by more than one tenant was refused. Rask cannot tell which " +
                "account was meant, so it signs in neither.");

            hasher.VerifyNothing(password);
            throttle.Hit(throttleKey);
            return Fail(AuthError.InvalidCredentials);
        }

        var user = candidates.Count == 1 ? candidates[0] : null;

        if (user is null)
        {
            // Hash anyway. Returning early on an unknown address makes the response measurably faster than one for a known
            // address, which turns this endpoint into an account-existence oracle.
            hasher.VerifyNothing(password);
            throttle.Hit(throttleKey);
            return Fail(AuthError.InvalidCredentials);
        }

        var check = hasher.Verify(user.PasswordHash, password);
        if (check == PasswordCheck.Failed)
        {
            throttle.Hit(throttleKey);
            return Fail(AuthError.InvalidCredentials);
        }

        throttle.Clear(throttleKey);

        if (check == PasswordCheck.SuccessRehashNeeded && hasher.Refuse(password) is null)
        {
            user.Rehash(hasher.Hash(password));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // Checked AFTER the password, deliberately. Answering "confirm your email address" to a wrong password would tell
        // anybody who asked that the address has an account here.
        if (options.RequireConfirmedEmail && !user.IsEmailConfirmed)
        {
            return Fail(AuthError.EmailNotConfirmed);
        }

        return new AccountOutcome(AuthResult.Success, AuthPrincipal.For(user));
    }

    /// <summary>Emails a reset link, if that address has an account.</summary>
    /// <remarks>
    /// Reports success either way. An endpoint that answered differently for an address registered here than for one that
    /// is not would be a membership oracle anybody could walk a list through.
    /// </remarks>
    public async Task<AuthResult> SendPasswordResetAsync(
        string email, string? client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        // Said out loud rather than swallowed. A reset that queues nothing looks exactly like one that worked, so an app with
        // no mail battery gets an error it can act on instead of a silence it cannot.
        if (!mail.IsConfigured)
        {
            return AuthResult.Fail(AuthError.MailNotConfigured);
        }

        var throttleKey = AuthThrottle.Key(ResetPurpose, email, client);
        if (throttle.IsThrottled(throttleKey))
        {
            return AuthResult.Fail(AuthError.TooManyAttempts);
        }

        throttle.Hit(throttleKey);

        var normalized = Authenticatable.NormalizeEmail(email);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.Set<TUser>().AsNoTracking().FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken)
                .ConfigureAwait(false) is { } user)
        {
            var sent = await mail
                .SendPasswordResetAsync(
                    user.Email, user.Id.ToString(), tokens.ForReset(user, options.TokenLifetime), cancellationToken)
                .ConfigureAwait(false);

            // Reported to the LOG, never to the caller (#1011): this branch is only reachable for an address that exists, so
            // answering differently here would make the page a membership oracle.
            if (!sent)
            {
                logger.LogError(
                    "A password reset could not be queued for a registered address. The caller was told the same thing "
                    + "every caller is told, so this line is the only place it appears. The usual cause is a mail battery "
                    + "whose tables are not in the DbContext model.");
            }
        }

        return AuthResult.Success;
    }

    /// <summary>Sets a new password from a token that arrived by email, and ends every session the user had.</summary>
    /// <remarks>
    /// If the reason for the reset was that somebody else had the password, their session ends here: its row is gone, so the
    /// next request, and the next dispatch on a live page, finds nobody signed in.
    /// </remarks>
    public async Task<AuthResult> ResetPasswordAsync(
        string userId, string token, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!Guid.TryParse(userId, out var id))
        {
            return AuthResult.Fail(AuthError.InvalidToken);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.Set<TUser>().FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false)
                is not { } user
            || !tokens.IsValidReset(user, token))
        {
            return AuthResult.Fail(AuthError.InvalidToken);
        }

        if (PasswordProblem(password) is { } problem)
        {
            return AuthResult.Fail(AuthError.WeakPassword, problem);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        user.ResetPassword(hasher.Hash(password), now);

        // A completed reset is also a confirmation: holding this token proves what the confirmation link proves, that
        // somebody read mail sent to that address.
        user.ConfirmEmail(now);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await sessions.EndAllAsync(user.Id, cancellationToken: cancellationToken).ConfigureAwait(false);

        return AuthResult.Success;
    }

    /// <summary>Marks an address confirmed from a token that arrived by email.</summary>
    public async Task<AuthResult> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(userId, out var id))
        {
            return AuthResult.Fail(AuthError.InvalidToken);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.Set<TUser>().FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false)
            is not { } user)
        {
            return AuthResult.Fail(AuthError.InvalidToken);
        }

        // Asked BEFORE checking the token (#1013): a second arrival at the page — a reload, a Back, a link opened twice —
        // should say the address is confirmed, not that the link did not work.
        if (user.IsEmailConfirmed)
        {
            return AuthResult.Fail(AuthError.EmailAlreadyConfirmed);
        }

        if (!tokens.IsValidConfirmation(user, token))
        {
            return AuthResult.Fail(AuthError.InvalidToken);
        }

        user.ConfirmEmail(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AuthResult.Success;
    }

    public Task<int> SignOutOtherDevicesAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
        AuthPrincipal.UserId(principal) is { } userId && AuthPrincipal.SessionId(principal) is { } sessionId
            ? sessions.EndAllAsync(userId, sessionId, cancellationToken)
            : Task.FromResult(0);

    public Task<int> SignOutEverywhereAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
        AuthPrincipal.UserId(principal) is { } userId
            ? sessions.EndAllAsync(userId, cancellationToken: cancellationToken)
            : Task.FromResult(0);

    /// <summary>Starts adding a passkey: the options the browser needs, and the sealed challenge it posts back.</summary>
    public async Task<PasskeyCreationChallenge?> BeginAddPasskeyAsync(
        Guid userId, string? origin, CancellationToken cancellationToken = default)
    {
        if (!options.Passkeys)
        {
            return null;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (await db.Set<TUser>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false) is not { } user)
        {
            return null;
        }

        // What the account already has, so the authenticator says "you already have a passkey here" instead of quietly
        // making a second one for the same device.
        var existing = await db.Set<Passkey>()
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.CredentialId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var challenge = PasskeyChallenges.NewChallenge();

        return new PasskeyCreationChallenge(
            challenges.Issue(PasskeyPurpose.Create, userId, challenge),
            WebEncoders.Base64UrlEncode(challenge),
            site.RelyingPartyId(origin),
            site.Name,

            // The account id as the user handle, never the address: it is stored on the authenticator, where anyone
            // holding the device may read it, and an id tells them nothing they did not already have.
            WebEncoders.Base64UrlEncode(userId.ToByteArray()),
            user.Email,
            user.Email,
            [.. existing.Select(WebEncoders.Base64UrlEncode)],
            (int)PasskeyChallenges.Lifetime.TotalMilliseconds);
    }

    /// <summary>Verifies a created passkey and stores it against the account that asked for it.</summary>
    public async Task<AuthResult> CompleteAddPasskeyAsync(
        Guid userId, PasskeyRegistrationRequest request, string? origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Passkeys)
        {
            return AuthResult.Fail(AuthError.NotAllowed);
        }

        // The challenge says which account asked. A state issued to somebody else is not a way into this one.
        if (challenges.Redeem(request.State, PasskeyPurpose.Create, out var owner) is not { } challenge
            || owner != userId)
        {
            return AuthResult.Fail(AuthError.InvalidToken);
        }

        if (PasskeyVerifier.VerifyRegistration(request, site.Ceremony(origin, challenge), out var failure)
            is not { } verified)
        {
            logger.LogDebug("A passkey registration was refused: {Failure}.", failure);
            return AuthResult.Fail(AuthError.PasskeyRejected, "That passkey could not be verified.");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var credentialId = verified.CredentialId;
        var taken = await db.Set<Passkey>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CredentialId == credentialId, cancellationToken)
            .ConfigureAwait(false);

        if (taken is { DeletedAt: null })
        {
            return AuthResult.Fail(AuthError.PasskeyRejected, "That passkey is already on an account.");
        }

        if (taken is not null)
        {
            // A removed passkey keeps its row, and a credential id is unique, so adding the same device again would
            // collide with its own tombstone forever. Registering it is what makes that row not worth keeping.
            await db.Set<Passkey>()
                .IgnoreQueryFilters()
                .Where(p => p.Id == taken.Id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var passkey = Passkey.Register(
            userId,
            request.Name,
            credentialId,
            verified.PublicKey,
            verified.Algorithm,
            verified.SignCount,
            verified.BackedUp,
            JoinTransports(request.Transports),
            clock.GetUtcNow().UtcDateTime);

        db.Add(passkey);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Two registrations of one credential arrived together and the unique index kept one.
            return AuthResult.Fail(AuthError.PasskeyRejected, "That passkey is already on an account.");
        }

        return AuthResult.Success;
    }

    /// <summary>Starts a passkey sign-in. Discoverable: nothing about any account leaves the server.</summary>
    public PasskeyRequestChallenge? BeginPasskeySignIn(string? origin)
    {
        if (!options.Passkeys)
        {
            return null;
        }

        var challenge = PasskeyChallenges.NewChallenge();

        return new PasskeyRequestChallenge(
            challenges.Issue(PasskeyPurpose.Get, Guid.Empty, challenge),
            WebEncoders.Base64UrlEncode(challenge),
            site.RelyingPartyId(origin),
            (int)PasskeyChallenges.Lifetime.TotalMilliseconds);
    }

    /// <summary>Verifies a signed challenge and produces the principal for the account that signed it.</summary>
    /// <remarks>
    /// Every failure answers <see cref="AuthError.InvalidCredentials" />, exactly as a wrong password does. Anybody can
    /// reach this endpoint, so "no such credential" and "that signature is wrong" have to be one answer.
    /// </remarks>
    public async Task<AccountOutcome> CompletePasskeySignInAsync(
        PasskeyLoginRequest request, string? origin, string? client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Passkeys)
        {
            return Fail(AuthError.NotAllowed);
        }

        var throttleKey = AuthThrottle.Key(PasskeyThrottlePurpose, "", client);
        if (throttle.IsThrottled(throttleKey))
        {
            return Fail(AuthError.TooManyAttempts);
        }

        if (challenges.Redeem(request.State, PasskeyPurpose.Get, out _) is not { } challenge)
        {
            throttle.Hit(throttleKey);
            return Fail(AuthError.InvalidCredentials);
        }

        byte[] credentialId;
        try
        {
            credentialId = WebEncoders.Base64UrlDecode(request.RawId);
        }
        catch (FormatException)
        {
            throttle.Hit(throttleKey);
            return Fail(AuthError.InvalidCredentials);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var passkey = await db.Set<Passkey>()
            .FirstOrDefaultAsync(p => p.CredentialId == credentialId, cancellationToken)
            .ConfigureAwait(false);

        if (passkey is null)
        {
            logger.LogDebug("A passkey sign-in was refused: {Failure}.", "no such credential");
            throttle.Hit(throttleKey);
            return Fail(AuthError.InvalidCredentials);
        }

        if (PasskeyVerifier.VerifyAssertion(request, site.Ceremony(origin, challenge), passkey, out var failure)
            is not { } verified)
        {
            logger.LogDebug("A passkey sign-in was refused: {Failure}.", failure);
            throttle.Hit(throttleKey);
            return Fail(AuthError.InvalidCredentials);
        }

        if (await db.Set<TUser>().FirstOrDefaultAsync(u => u.Id == passkey.UserId, cancellationToken)
                .ConfigureAwait(false) is not { } user)
        {
            throttle.Hit(throttleKey);
            return Fail(AuthError.InvalidCredentials);
        }

        throttle.Clear(throttleKey);

        if (options.RequireConfirmedEmail && !user.IsEmailConfirmed)
        {
            return Fail(AuthError.EmailNotConfirmed);
        }

        passkey.Used(verified.SignCount, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AccountOutcome(AuthResult.Success, AuthPrincipal.For(user));
    }

    /// <summary>Removes one of an account's passkeys. It stops signing anybody in at once.</summary>
    public async Task<AuthResult> RemovePasskeyAsync(
        Guid userId, Guid passkeyId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Scoped to the caller's own account, so an id guessed from somewhere else removes nothing.
        if (await db.Set<Passkey>()
                .FirstOrDefaultAsync(p => p.Id == passkeyId && p.UserId == userId, cancellationToken)
                .ConfigureAwait(false) is not { } passkey)
        {
            return AuthResult.Fail(AuthError.PasskeyRejected, "That passkey is not on this account.");
        }

        passkey.Removed();
        db.Remove(passkey);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AuthResult.Success;
    }

    private static string? JoinTransports(IReadOnlyList<string>? transports) =>
        transports is { Count: > 0 } ? string.Join(',', transports) : null;

    private string? PasswordProblem(string password) =>
        password.Length < options.MinimumPasswordLength
            ? $"Passwords must be at least {options.MinimumPasswordLength} characters."
            : hasher.Refuse(password);

    private static bool IsEmail(string email) =>
        email.Trim() is { Length: > 0 and <= 256 } trimmed
        && MailAddress.TryCreate(trimmed, out var address)
        && string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);

    private static AccountOutcome Fail(AuthError error) => new(AuthResult.Fail(error), null);
}
