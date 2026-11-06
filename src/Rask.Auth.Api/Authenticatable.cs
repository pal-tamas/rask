using System.Security.Claims;
using Rask.Data;

namespace Rask.Auth;

/// <summary>
/// The base class of the application's <c>User</c>, keyed by a <typeparamref name="TKey" />: the credentials Rask.Auth
/// needs, on an aggregate the app owns.
/// </summary>
/// <typeparam name="TKey">
/// The key: a <see cref="Guid" />, or a <see cref="string" /> holding one in the form <c>Guid.ToString()</c> writes — the
/// shape a users table another framework made often has. Rask carries the signed-in user as that <see cref="Guid" />
/// (<c>Current.UserId</c>, a session's <c>UserId</c>, the events), so any other key is refused when the app starts.
/// </typeparam>
/// <remarks>
/// <para>
/// Most apps derive from <see cref="Authenticatable" />, which is this keyed by a <see cref="Guid" />. Derive from this one
/// for a users table keyed by text, and mark the property people sign in with
/// <see cref="SignInNameAttribute">[SignInName]</see> when it is not the address:
/// <code>
/// public sealed class User : Authenticatable&lt;string&gt;
/// {
///     [SignInName] public Username Username { get; private set; }
/// }
/// </code>
/// </para>
/// <para>
/// An app declares one type deriving from it (<c>rask new</c> writes <c>public sealed class User : Authenticatable</c>
/// into <c>Features/Shared</c>), and adds its own columns beside these. A source generator finds it, so
/// <c>AddRaskAuth()</c> and <c>modelBuilder.AddRaskAuth()</c> need no type argument.
/// </para>
/// <para>
/// It says <see cref="IUser" />, so the app's <c>User</c> is <c>Current.User</c> — its read face, <c>UserRead</c> — with
/// no line of its own.
/// </para>
/// <para>
/// The credentials change only through Rask.Auth: registering, resetting and confirming go through <c>IAuth</c>, which
/// hashes, checks tokens and ends sessions. They have private setters and are never on the generated form model. Roles
/// are the app's to change: <c>User.Update(id, u =&gt; u.GrantRole("editor"))</c>.
/// </para>
/// </remarks>
public abstract class Authenticatable<TKey> : Aggregate<TKey, Timestamps, Versioned, Model, Deletable>, IUser
    where TKey : notnull
{
    /// <summary>
    /// The address, trimmed and lower-cased. What the account signs in with, and unique — unless a property of the app's
    /// says <see cref="SignInNameAttribute">[SignInName]</see>, when it is empty: such an account has no address.
    /// </summary>
    public string Email { get; private set; } = "";

    // The password hash. Internal, so nothing that serializes a User's public properties (an API response, a data
    // grid) can ever carry it; Rask.Auth maps it explicitly.
    internal string PasswordHash { get; private set; } = "";

    /// <summary>When the address was confirmed, or <see langword="null" /> while it has not been.</summary>
    public DateTime? EmailConfirmedAt { get; private set; }

    /// <summary>When the password last changed, or <see langword="null" /> when it never has since registering.</summary>
    public DateTime? PasswordChangedAt { get; private set; }

    /// <summary>The roles this user holds, such as <c>admin</c>. The principal carries each one.</summary>
    public IReadOnlyList<string> Roles { get; private set; } = [];

    /// <summary>The tenant this user belongs to, or <see langword="null" /> for one who belongs to none, such as an administrator.</summary>
    /// <remarks>
    /// The principal carries it, which is how every tenant-scoped read and write of a signed-in page finds its
    /// tenant. Set it with <see cref="RecordTenant" />.
    /// </remarks>
    public Guid? TenantId { get; private set; }

    /// <summary>Whether the address has been confirmed.</summary>
    public bool IsEmailConfirmed => EmailConfirmedAt is not null;

    /// <summary>Records which tenant this user belongs to.</summary>
    /// <param name="tenant">The tenant, or <see langword="null" /> for a user who belongs to none.</param>
    /// <remarks>
    /// <c>protected</c>, so the app's <c>User</c> decides when that happens — an invitation accepted, a company
    /// signing up — behind a method of its own: <c>public void JoinTenant(Guid tenant) =&gt; RecordTenant(tenant);</c>
    /// </remarks>
    protected void RecordTenant(Guid? tenant) => TenantId = tenant;

    /// <summary>Whether this user holds <paramref name="role" />.</summary>
    /// <param name="role">The role name.</param>
    /// <returns><see langword="true" /> when it is among <see cref="Roles" />.</returns>
    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.Ordinal);

    /// <summary>Gives this user <paramref name="role" />. Holding it already changes nothing.</summary>
    /// <param name="role">The role name.</param>
    public void GrantRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (!IsInRole(role))
        {
            Roles = [.. Roles, role];
        }
    }

    /// <summary>Takes <paramref name="role" /> away. Not holding it changes nothing.</summary>
    /// <param name="role">The role name.</param>
    public void RevokeRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (IsInRole(role))
        {
            Roles = [.. Roles.Where(r => !string.Equals(r, role, StringComparison.Ordinal))];
        }
    }

    /// <summary>
    /// Claims of this user's own, carried by the principal Rask.Auth issues: on the session a cookie points at and
    /// in a bearer token alike.
    /// </summary>
    /// <remarks>
    /// Read when the user signs in and each time their session is loaded again — at every request, and before every
    /// event of a live page — so a change to the row reaches a signed-in user at their next action. The claims Rask.Auth issues itself — the id,
    /// the name, the address, the roles, the session and the tenant — are not this method's to give, and naming one
    /// of them throws.
    /// </remarks>
    /// <returns>The extra claims; none by default.</returns>
    protected virtual IEnumerable<Claim> Claims() => [];

    // Checked here rather than where the principal is built, so sign-in and a resumed session refuse the same way.
    internal OwnClaim[] OwnClaims()
    {
        var claims = new List<OwnClaim>();
        foreach (var claim in Claims())
        {
            if (AuthPrincipal.Issues(claim.Type))
            {
                throw new InvalidOperationException(
                    $"{GetType().Name}.{nameof(Claims)}() returned a '{claim.Type}' claim, which Rask.Auth issues itself. "
                    + "Give roles with GrantRole, and leave the id, name, address, session and tenant to the account.");
            }

            claims.Add(new OwnClaim(claim.Type, claim.Value));
        }

        return [.. claims];
    }

    /// <summary>The <see cref="Guid" /> Rask carries this account as: its key, or the one its text key holds.</summary>
    internal Guid AccountId => AccountKey<TKey>.Of(Id);

    // Empty for an account that signs in by name: the app's own setter (IAuth.Register's apply) has written the name.
    internal void Register(string email, string passwordHash)
    {
        Id = AccountKey<TKey>.New();
        Email = email.Length == 0 ? "" : Authenticatable.NormalizeEmail(email);
        PasswordHash = passwordHash;
        Raise(new UserRegistered(AccountId, Email));
    }

    internal void ChangePasswordHash(string passwordHash, DateTime now)
    {
        PasswordHash = passwordHash;
        PasswordChangedAt = now;
    }

    // A rehash on sign-in is the same password under stronger parameters, so it is not a password change.
    internal void Rehash(string passwordHash) => PasswordHash = passwordHash;

    internal void ConfirmEmail(DateTime now)
    {
        if (EmailConfirmedAt is null)
        {
            EmailConfirmedAt = now;
            Raise(new EmailConfirmed(AccountId, Email));
        }
    }

    internal void ResetPassword(string passwordHash, DateTime now)
    {
        ChangePasswordHash(passwordHash, now);
        Raise(new PasswordReset(AccountId));
    }

    // An administrator's new account belongs to the tenant they are working in (IAccounts.Create).
    internal void JoinTenantInFlight(Guid? tenant) => RecordTenant(tenant);

    // A new address is not a proved one: it is confirmed again, by whoever reads mail sent to it.
    internal void ChangeEmail(string normalized)
    {
        if (!string.Equals(Email, normalized, StringComparison.Ordinal))
        {
            Email = normalized;
            EmailConfirmedAt = null;
        }
    }
}

/// <summary>
/// The base class of the application's <c>User</c>: the credentials Rask.Auth needs, on an aggregate the app owns, keyed
/// by a <see cref="Guid" /> and signing in with its address.
/// </summary>
/// <remarks>
/// What <c>rask new</c> writes: <c>public sealed class User : Authenticatable</c>. Every member is
/// <see cref="Authenticatable{TKey}" />'s; a users table keyed by text derives from that one instead.
/// </remarks>
public abstract class Authenticatable : Authenticatable<Guid>
{
    /// <summary>Normalizes an address the way <see cref="Authenticatable{TKey}.Email" /> stores it.</summary>
    /// <param name="email">The address as typed.</param>
    /// <returns>The trimmed, lower-cased address.</returns>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }
}
