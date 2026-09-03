using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rask.Core.Authentication;

namespace Rask.Auth;

/// <summary>What an account operation produced: the outcome, and the principal when it succeeded.</summary>
internal sealed record AccountOutcome(AuthResult Result, ClaimsPrincipal? Principal);

/// <summary>
/// The account store, without its user type.
/// </summary>
/// <remarks>
/// The endpoints are mapped by <c>MapRaskAuth()</c>, which has no way to know which
/// <see cref="RaskUser" /> an app configured — it is a parameterless extension method on the endpoint
/// builder, chosen so an app writes one line. Registering this alongside the generic service gives the
/// endpoints something to resolve that does not name the type.
/// </remarks>
internal interface IAccounts
{
    Task<AccountOutcome> RegisterAsync(
        string email, string password, string? firstRunToken, CancellationToken cancellationToken = default);

    Task<AccountOutcome> ValidateAsync(string email, string password);
}

/// <summary>
/// Register and password-check against the account store, and nothing else.
/// </summary>
/// <remarks>
/// It deliberately stops at producing a <see cref="ClaimsPrincipal"/> and never issues a session,
/// because the two callers issue one differently and both are correct: a component handler on the
/// Server host has no <c>HttpContext</c> to write a cookie on and must go through
/// <see cref="IAuthSignIn"/>'s ticket relay, while the <c>/api/auth</c> endpoints are ordinary HTTP and
/// call <c>HttpContext.SignInAsync</c> directly. Keeping the store logic here means the two paths cannot
/// drift in what they consider a valid registration.
/// </remarks>
/// <typeparam name="TUser">The application's user entity.</typeparam>
internal sealed class AccountService<TUser>(
    UserManager<TUser> users,
    SignInManager<TUser> signIn,
    IRoleSeedContexts contexts,
    IInstanceClaimStore claims,
    FirstRunToken firstRun,
    AuthOptions options,
    TimeProvider clock) : IAccounts
    where TUser : RaskUser, new()
{
    public async Task<AccountOutcome> RegisterAsync(
        string email,
        string password,
        string? firstRunToken,
        CancellationToken cancellationToken = default)
    {
        // The gate applies only while the instance is unclaimed. Asking the database rather than the
        // in-memory token means a restart cannot re-open the window on an app that already has accounts.
        if (options.FirstUserIsAdmin
            && options.RequireFirstRunToken
            && !await claims.IsClaimedAsync(cancellationToken).ConfigureAwait(false)
            && !firstRun.Matches(firstRunToken))
        {
            return new AccountOutcome(AuthResult.Fail(AuthError.FirstRunTokenRequired), null);
        }

        // Both roles, before any account exists to hold one. Seeded here rather than at startup because a
        // freshly scaffolded app boots before its first migration has run, and a hosted service writing
        // to a table that does not exist yet would stop it starting at all.
        //
        // Before the user is created rather than after, and BOTH rather than the one this registration
        // needs: AddToRoleAsync below resolves the role itself and hits the same unique index, and it is
        // Identity's call, so there is no result to inspect — a lost race throws out of it. Seeding first
        // means that by the time any racer assigns a role, the row it needs is already there.
        await EnsureRolesAsync(cancellationToken).ConfigureAwait(false);

        var user = new TUser
        {
            UserName = email,
            Email = email,
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
        };

        var created = await users.CreateAsync(user, password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return new AccountOutcome(Translate(created), null);
        }

        // Winning the claim is what makes this account the administrator, and only one caller can win
        // it — the row's primary key is a constant. A loser here is not an error: it is the second
        // person to register, and they get the ordinary role.
        var isAdmin = options.FirstUserIsAdmin
                      && await claims.TryClaimAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var role = isAdmin ? RaskRoles.Admin : RaskRoles.User;
        await users.AddToRoleAsync(user, role).ConfigureAwait(false);

        if (isAdmin)
        {
            firstRun.Clear();
        }

        // Built after the role is assigned, so the principal carries it.
        var principal = await signIn.CreateUserPrincipalAsync(user).ConfigureAwait(false);
        return new AccountOutcome(AuthResult.Success, principal);
    }

    public async Task<AccountOutcome> ValidateAsync(string email, string password)
    {
        var user = await users.FindByEmailAsync(email).ConfigureAwait(false);

        if (user is null)
        {
            // Hash anyway. Returning early on an unknown address makes the response measurably faster
            // than one for a known address, which turns this endpoint into an account-existence oracle.
            users.PasswordHasher.HashPassword(new TUser(), password);
            return new AccountOutcome(AuthResult.Fail(AuthError.InvalidCredentials), null);
        }

        var result = await signIn
            .CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)
            .ConfigureAwait(false);

        if (result.IsLockedOut)
        {
            return new AccountOutcome(AuthResult.Fail(AuthError.LockedOut), null);
        }

        if (result.IsNotAllowed)
        {
            return new AccountOutcome(AuthResult.Fail(AuthError.NotAllowed), null);
        }

        if (!result.Succeeded)
        {
            return new AccountOutcome(AuthResult.Fail(AuthError.InvalidCredentials), null);
        }

        var principal = await signIn.CreateUserPrincipalAsync(user).ConfigureAwait(false);
        return new AccountOutcome(AuthResult.Success, principal);
    }

    /// <summary>Creates the built-in roles if they are not there yet, tolerating a lost race.</summary>
    /// <remarks>
    /// <para>
    /// Written against a context of its own rather than the <see cref="RoleManager{TRole}" />, and that is
    /// the whole point. <c>RoleManager</c> resolves the same scoped <c>DbContext</c> the
    /// <see cref="UserManager{TUser}" /> uses, so when a racer loses this race its rejected
    /// <c>AspNetRoles</c> row stays in the change tracker as <c>Added</c> — and the next
    /// <c>SaveChanges</c> on that context, the user insert, retries it and throws there. The stack then
    /// blames <c>UserManager.CreateAsync</c> for a constraint on a table it never touched, which is a
    /// long way from the cause.
    /// </para>
    /// <para>
    /// Check-then-insert is not atomic, so several registrations arriving together can all find a role
    /// missing and all try to add it. The unique index on the normalized name keeps it single; every
    /// loser is a non-event, because the row it wanted is there.
    /// </para>
    /// </remarks>
    private async Task EnsureRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var role in RaskRoles.All)
        {
            var normalized = role.ToUpperInvariant();

            await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var set = db.Set<IdentityRole>();

            if (await set.AnyAsync(r => r.NormalizedName == normalized, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            set.Add(new IdentityRole(role)
            {
                NormalizedName = normalized,
                ConcurrencyStamp = Guid.NewGuid().ToString(),
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Somebody else created it between the check and the insert. The row exists, which is
                // all this method promised — and the context carrying the rejected entry is disposed on
                // the way out of this iteration, so nothing downstream inherits it.
            }
        }
    }

    /// <summary>Turns Identity's error codes into Rask's, so a page can say them in its own language.</summary>
    private static AuthResult Translate(IdentityResult result)
    {
        var errors = result.Errors.ToArray();

        if (Array.Exists(errors, e => e.Code.StartsWith("Duplicate", StringComparison.Ordinal)))
        {
            return AuthResult.Fail(AuthError.DuplicateAccount);
        }

        if (Array.Exists(errors, e => e.Code.Contains("Email", StringComparison.Ordinal)))
        {
            return AuthResult.Fail(AuthError.InvalidEmail);
        }

        // Everything else Identity rejects a new account for is a password-policy failure. The
        // descriptions are worth carrying: "must be at least 8 characters" is actionable in a way that
        // a bare code is not.
        var message = string.Join(" ", errors.Select(e => e.Description));
        return AuthResult.Fail(AuthError.WeakPassword, message.Length == 0 ? null : message);
    }
}
