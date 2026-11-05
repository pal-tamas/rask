namespace Rask.Auth.SignInName.Tests;

/// <summary>A user name: what the people of an app moved onto Rask sign in with.</summary>
/// <param name="Value">The name as stored.</param>
public readonly record struct Username(string Value);

/// <summary>
///     The account of an app whose users table another framework made: keyed by a Guid kept as text, signing in by
///     user name, its passwords hashed with bcrypt.
/// </summary>
public sealed class User : Authenticatable<string>
{
    /// <summary>What the user signs in with.</summary>
    [SignInName]
    public Username Username { get; private set; }

    /// <summary>An empty row, for a test that writes one as the table it moved from holds it.</summary>
    /// <returns>A user with nothing set.</returns>
    public static User Blank() => new();

    /// <summary>Moves the user into <paramref name="tenant" />.</summary>
    /// <param name="tenant">The tenant.</param>
    public void JoinTenant(Guid tenant) => RecordTenant(tenant);
}
