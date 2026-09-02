using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Rask.Core.Authentication;

namespace Rask.Auth;

/// <summary>What an account operation produced: the outcome, and the principal when it succeeded.</summary>
internal sealed record AccountOutcome(AuthResult Result, ClaimsPrincipal? Principal);

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
    RoleManager<IdentityRole> roles,
    IInstanceClaimStore claims,
    FirstRunToken firstRun,
    AuthOptions options,
    TimeProvider clock)
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

        // Seeded here rather than at startup on purpose: a freshly scaffolded app boots before its
        // first migration has run, and a hosted service that writes to a table which does not exist
        // yet would stop the app from starting at all. Nothing can hold a role before the first
        // registration, so first use is the earliest moment the seeding is actually needed.
        await EnsureRoleAsync(role).ConfigureAwait(false);
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

    private async Task EnsureRoleAsync(string role)
    {
        if (!await roles.RoleExistsAsync(role).ConfigureAwait(false))
        {
            // A concurrent registration may create it between the check and the write; the unique
            // index on the normalized name is what actually keeps it single, so a failure here is only
            // interesting if the role still does not exist afterwards.
            var created = await roles.CreateAsync(new IdentityRole(role)).ConfigureAwait(false);

            if (!created.Succeeded && !await roles.RoleExistsAsync(role).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"The '{role}' role could not be created: "
                    + string.Join(" ", created.Errors.Select(e => e.Description)));
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
