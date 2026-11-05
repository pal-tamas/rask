using System.Net.Mail;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>
/// What a sign-in name and a password must be — one rule for an account a person registers and one an administrator
/// makes or changes.
/// </summary>
internal static class AccountChecks
{
    /// <summary>Why <paramref name="typed" /> cannot be what an account of <typeparamref name="TUser" /> signs in with, or null.</summary>
    /// <typeparam name="TUser">The account type.</typeparam>
    /// <typeparam name="TKey">Its key type.</typeparam>
    /// <param name="typed">The name or address as typed.</param>
    internal static AuthResult? NameProblem<TUser, TKey>(string typed)
        where TUser : Authenticatable<TKey>
        where TKey : notnull
    {
        if (!AccountName<TUser, TKey>.IsEmail)
        {
            return IsName(typed, AccountName<TUser, TKey>.MaxLength)
                ? null
                : AuthResult.Fail(AuthError.InvalidEmail, $"A name is 1 to {AccountName<TUser, TKey>.MaxLength} characters.");
        }

        return IsEmail(typed) ? null : AuthResult.Fail(AuthError.InvalidEmail);
    }

    /// <summary>Why <paramref name="password" /> is refused — too short, or refused by the hasher — or null.</summary>
    /// <param name="password">The password as typed.</param>
    /// <param name="options">The minimum length.</param>
    /// <param name="hasher">What else a password must be.</param>
    internal static string? PasswordProblem(string password, AuthOptions options, PasswordHasher hasher) =>
        password.Length < options.MinimumPasswordLength
            ? $"Passwords must be at least {options.MinimumPasswordLength} characters."
            : hasher.Refuse(password);

    private static bool IsName(string name, int maxLength) =>
        name.Trim().Length is var length && length > 0 && length <= maxLength;

    private static bool IsEmail(string email) =>
        email.Trim() is { Length: > 0 and <= 256 } trimmed
        && MailAddress.TryCreate(trimmed, out var address)
        && string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);
}
