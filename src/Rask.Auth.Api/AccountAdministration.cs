using Microsoft.EntityFrameworkCore;
using Rask.Cqrs;
using Rask.Data;
using Rask.Wire;

namespace Rask.Auth;

/// <inheritdoc cref="IAccounts" />
/// <remarks>
/// <para>
/// Who may is read from the acting user's ROW, as choosing a tenant is: a role taken away a moment ago is not one they
/// still hold, whatever the principal they carry says. The page's <c>[Authorize]</c> is the first gate and this the
/// second, so an admin page that lost its attribute in a refactor still hands nothing to a user who is not one.
/// </para>
/// <para>
/// Every change is one <c>SaveChanges</c> on one context, so it lands whole or not at all — and inside a
/// <c>Db.Transaction</c> that context joins it, so it is rolled back with the rest of the work.
/// </para>
/// </remarks>
/// <typeparam name="TUser">The application's user aggregate.</typeparam>
/// <typeparam name="TKey">Its key: a Guid, or a string holding one.</typeparam>
internal sealed class AccountAdministration<TUser, TKey>(
    IAuthContexts contexts,
    AuthOptions options,
    PasswordHasher hasher,
    AuthMail mail,
    AuthTokens tokens,
    TimeProvider clock) : IAccounts
    where TUser : Authenticatable<TKey>, new()
    where TKey : notnull
{
    public Task<AuthResult> Create(string name, string password, CancellationToken cancellationToken = default) =>
        CreateAccount(name, password, apply: null, cancellationToken);

    public Task<AuthResult> Create<T>(
        string name, string password, Action<T> apply, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(apply);

        if (!typeof(T).IsAssignableFrom(typeof(TUser)))
        {
            throw new ArgumentException(
                $"The new user is a {typeof(TUser).Name}, which is not a {typeof(T).Name}.", nameof(apply));
        }

        return CreateAccount(name, password, user => apply((T)(object)user), cancellationToken);
    }

    private async Task<AuthResult> CreateAccount(
        string name, string password, Action<TUser>? apply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken = Ambient.Or(cancellationToken);

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await RequireAdministratorAsync(db, cancellationToken).ConfigureAwait(false);

            if (await NameRefusalAsync(db, name, except: null, cancellationToken).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }

            if (AccountChecks.PasswordProblem(password, options, hasher) is { } problem)
            {
                return AuthResult.Fail(AuthError.WeakPassword, problem);
            }

            var normalized = AccountName<TUser, TKey>.Normalize(name);
            var user = new TUser();
            user.Register(AccountName<TUser, TKey>.IsEmail ? normalized : "", hasher.Hash(password));
            user.JoinTenantInFlight(Tenant.Key);
            user.GrantRole(RaskRoles.User);
            apply?.Invoke(user);

            db.Add(user);
            AccountName<TUser, TKey>.Write(db, user, normalized);

            if (!await SaveAsync(db, name, except: null, cancellationToken).ConfigureAwait(false))
            {
                return AuthResult.Fail(AuthError.DuplicateAccount);
            }

            await SendConfirmationAsync(user, cancellationToken).ConfigureAwait(false);
            return AuthResult.Success;
        }
    }

    public async Task<AuthResult> Rename(Guid id, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken = Ambient.Or(cancellationToken);

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var user = await ManagedAsync(db, id, cancellationToken).ConfigureAwait(false);

            if (await NameRefusalAsync(db, name, except: id, cancellationToken).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }

            var normalized = AccountName<TUser, TKey>.Normalize(name);
            var newAddress = AccountName<TUser, TKey>.IsEmail && !string.Equals(user.Email, normalized, StringComparison.Ordinal);
            if (AccountName<TUser, TKey>.IsEmail)
            {
                user.ChangeEmail(normalized);
            }
            else
            {
                AccountName<TUser, TKey>.Write(db, user, normalized);
            }

            if (!await SaveAsync(db, name, except: id, cancellationToken).ConfigureAwait(false))
            {
                return AuthResult.Fail(AuthError.DuplicateAccount);
            }

            if (newAddress)
            {
                await SendConfirmationAsync(user, cancellationToken).ConfigureAwait(false);
            }

            return AuthResult.Success;
        }
    }

    public async Task<AuthResult> SetPassword(Guid id, string? password, CancellationToken cancellationToken = default)
    {
        cancellationToken = Ambient.Or(cancellationToken);

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var user = await ManagedAsync(db, id, cancellationToken).ConfigureAwait(false);

            // An edit form's empty password field: the account keeps the one it has, and stays signed in.
            if (string.IsNullOrWhiteSpace(password))
            {
                return AuthResult.Success;
            }

            if (AccountChecks.PasswordProblem(password, options, hasher) is { } problem)
            {
                return AuthResult.Fail(AuthError.WeakPassword, problem);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            user.ChangePasswordHash(hasher.Hash(password), now);

            // In the same save as the password, so there is no moment in which the new password is set and a sign-in
            // made with the old one still works. The acting administrator's own sessions are never among them: an
            // administrator's account is not one this reaches.
            await EndSessionsAsync(db, user.AccountId, now, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return AuthResult.Success;
        }
    }

    public async Task Remove(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken = Ambient.Or(cancellationToken);

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var user = await ManagedAsync(db, id, cancellationToken).ConfigureAwait(false);
            var accountId = user.AccountId;
            var now = clock.GetUtcNow().UtcDateTime;

            // Every way in goes with the account: an ended session is not resumed, and is swept with the others a day
            // later (SessionSweep); a passkey row would otherwise outlive the account it signs in to.
            await EndSessionsAsync(db, accountId, now, cancellationToken).ConfigureAwait(false);

            foreach (var passkey in await db.Set<Passkey>()
                         .Where(p => p.UserId == accountId)
                         .ToListAsync(cancellationToken)
                         .ConfigureAwait(false))
            {
                passkey.Removed();
                db.Remove(passkey);
            }

            db.Remove(user);

            // One save: the user, their sessions and their passkeys go together or not at all.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // The signed-in user, read from their row, holds the administrator role — or nothing here runs.
    private static async Task RequireAdministratorAsync(DbContext db, CancellationToken cancellationToken)
    {
        if (Current.UserId is not { } acting)
        {
            throw new ForbiddenException(
                "Managing accounts takes a signed-in administrator, and nobody is signed in.", isAuthenticated: false);
        }

        var actor = await db.Set<TUser>()
            .AsNoTracking()
            .FirstOrDefaultAsync(AccountKey<TKey>.Is<TUser>(acting), cancellationToken)
            .ConfigureAwait(false);

        if (actor is null || !actor.IsInRole(RaskRoles.Admin))
        {
            throw new ForbiddenException(
                $"Managing accounts takes the '{RaskRoles.Admin}' role, which the signed-in user does not hold.",
                isAuthenticated: true);
        }
    }

    // The account an administrator may manage: of the tenant in flight, and not an administrator's. Anything else is
    // one answer, so a guessed id tells nobody whether it is another tenant's.
    private static async Task<TUser> ManagedAsync(DbContext db, Guid id, CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(db, cancellationToken).ConfigureAwait(false);

        var tenant = Tenant.Key;
        var user = await db.Set<TUser>()
            .FirstOrDefaultAsync(AccountKey<TKey>.Is<TUser>(id), cancellationToken)
            .ConfigureAwait(false);

        return user is not null && user.TenantId == tenant && !user.IsInRole(RaskRoles.Admin)
            ? user
            : throw AccountNotFound.For(id);
    }

    // Why the name cannot be this account's — not a name, or another account's — or null. Taken in ANY tenant, as
    // registering checks it: sign-in finds an account before it knows its tenant, and refuses a name two hold.
    private static async Task<AuthResult?> NameRefusalAsync(
        DbContext db, string name, Guid? except, CancellationToken cancellationToken)
    {
        if (AccountChecks.NameProblem<TUser, TKey>(name) is { } problem)
        {
            return problem;
        }

        var holders = await db.Set<TUser>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(AccountName<TUser, TKey>.Is(db, AccountName<TUser, TKey>.Normalize(name)))
            .Select(u => u.Id)
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return holders.Exists(key => AccountKey<TKey>.Of(key) != except) ? AuthResult.Fail(AuthError.DuplicateAccount) : null;
    }

    // Ends them as signing out does, staged on the caller's context so they are saved with what ended them.
    private static async Task EndSessionsAsync(DbContext db, Guid accountId, DateTime now, CancellationToken cancellationToken)
    {
        foreach (var session in await db.Set<Session>()
                     .Where(s => s.UserId == accountId)
                     .ToListAsync(cancellationToken)
                     .ConfigureAwait(false))
        {
            session.End(now);
            db.Remove(session);
        }
    }

    // False when the unique index refused the name: somebody took it between the check and the save. Any other
    // refusal — a column of the app's own left unset — is the app's to see, not a "taken" it would go looking for.
    private static async Task<bool> SaveAsync(DbContext db, string name, Guid? except, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            if (await NameRefusalAsync(db, name, except, cancellationToken).ConfigureAwait(false) is { Error: AuthError.DuplicateAccount })
            {
                return false;
            }

            throw;
        }
    }

    // An address is sent the link that proves it, as registering sends one; a name has nowhere to send it.
    private async Task SendConfirmationAsync(TUser user, CancellationToken cancellationToken)
    {
        if (AccountName<TUser, TKey>.IsEmail)
        {
            await mail
                .SendConfirmationAsync(
                    user.Email, user.AccountId.ToString(), tokens.ForConfirmation(user, options.TokenLifetime), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
