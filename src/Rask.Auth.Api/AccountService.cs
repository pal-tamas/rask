using System.Net.Mail;
using System.Security.Claims;
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
    IAuthSessions sessions,
    TimeProvider clock,
    ILogger<AccountService<TUser>> logger) : IAccounts
    where TUser : Authenticatable, new()
{
    private const string SignInPurpose = "sign-in";
    private const string RegisterPurpose = "register";
    private const string ResetPurpose = "reset";

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
        var user = await db.Set<TUser>()
            .FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken)
            .ConfigureAwait(false);

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
