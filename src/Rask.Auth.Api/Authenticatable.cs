using Rask.Data;

namespace Rask.Auth;

/// <summary>
/// The base class of the application's <c>User</c>: the credentials Rask.Auth needs, on an aggregate the app owns.
/// </summary>
/// <remarks>
/// <para>
/// An app declares one type deriving from it (<c>rask new</c> writes <c>public sealed class User : Authenticatable</c>
/// into <c>Features/Shared</c>), and adds its own columns beside these. A source generator finds it, so
/// <c>AddRaskAuth()</c> and <c>modelBuilder.AddRaskAuth()</c> need no type argument.
/// </para>
/// <para>
/// The credentials change only through Rask.Auth: registering, resetting and confirming go through <c>IAuth</c>, which
/// hashes, checks tokens and ends sessions. They have private setters and are never on the generated form model. Roles
/// are the app's to change: <c>User.Update(id, u =&gt; u.GrantRole("editor"))</c>.
/// </para>
/// </remarks>
public abstract class Authenticatable : Aggregate<Guid>
{
    /// <summary>The sign-in address, trimmed and lower-cased. Unique.</summary>
    public string Email { get; private set; } = "";

    // The password hash. Internal, so nothing that serializes a User's public properties (an API response, an island
    // prop, a data grid) can ever carry it; Rask.Auth maps it explicitly.
    internal string PasswordHash { get; private set; } = "";

    /// <summary>When the address was confirmed, or <see langword="null" /> while it has not been.</summary>
    public DateTime? EmailConfirmedAt { get; private set; }

    /// <summary>When the password last changed, or <see langword="null" /> when it never has since registering.</summary>
    public DateTime? PasswordChangedAt { get; private set; }

    /// <summary>The roles this user holds, such as <c>admin</c>. The principal carries each one.</summary>
    public IReadOnlyList<string> Roles { get; private set; } = [];

    /// <summary>Whether the address has been confirmed.</summary>
    public bool IsEmailConfirmed => EmailConfirmedAt is not null;

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

    /// <summary>Normalizes an address the way <see cref="Email" /> stores it.</summary>
    /// <param name="email">The address as typed.</param>
    /// <returns>The trimmed, lower-cased address.</returns>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    internal void Register(string email, string passwordHash, DateTime now)
    {
        Id = Guid.CreateVersion7();
        Email = NormalizeEmail(email);
        PasswordHash = passwordHash;
        Raise(new UserRegistered(Id, Email));
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
            Raise(new EmailConfirmed(Id, Email));
        }
    }

    internal void ResetPassword(string passwordHash, DateTime now)
    {
        ChangePasswordHash(passwordHash, now);
        Raise(new PasswordReset(Id));
    }
}
