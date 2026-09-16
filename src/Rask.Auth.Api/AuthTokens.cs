using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Rask.Auth;

/// <summary>
/// The links in confirmation and reset emails: signed, expiring, and dead the moment they have done their job.
/// </summary>
/// <remarks>
/// <para>
/// Stateless, like Rails' <c>generates_token_for</c>: nothing is stored. The token is a Data Protection payload carrying
/// its purpose, the user's id, when it expires, and a fingerprint of the state it is allowed to change: the password hash
/// for a reset, the address and its confirmation for a confirm. Using the token changes that state, so the same link does
/// not work twice, and a password changed any other way kills every outstanding reset link too.
/// </para>
/// <para>
/// The expiry is checked against the app's <see cref="TimeProvider" /> rather than Data Protection's own clock, so a test
/// on a fake clock sees the lifetime it set.
/// </para>
/// </remarks>
internal sealed class AuthTokens(IDataProtectionProvider provider, TimeProvider clock)
{
    private const string ProtectorPurpose = "Rask.Auth.Tokens.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(ProtectorPurpose);

    /// <summary>A reset link token for <paramref name="user" />.</summary>
    public string ForReset(Authenticatable user, TimeSpan lifetime) =>
        Issue(TokenPurpose.Reset, user.Id, ResetFingerprint(user), lifetime);

    /// <summary>A confirmation link token for <paramref name="user" />.</summary>
    public string ForConfirmation(Authenticatable user, TimeSpan lifetime) =>
        Issue(TokenPurpose.Confirm, user.Id, ConfirmFingerprint(user), lifetime);

    /// <summary>Whether <paramref name="token" /> is a live reset token for <paramref name="user" />.</summary>
    public bool IsValidReset(Authenticatable user, string token) =>
        IsValid(token, TokenPurpose.Reset, user.Id, ResetFingerprint(user));

    /// <summary>Whether <paramref name="token" /> is a live confirmation token for <paramref name="user" />.</summary>
    public bool IsValidConfirmation(Authenticatable user, string token) =>
        IsValid(token, TokenPurpose.Confirm, user.Id, ConfirmFingerprint(user));

    private string Issue(TokenPurpose purpose, Guid userId, string fingerprint, TimeSpan lifetime)
    {
        var expires = (clock.GetUtcNow() + lifetime).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{(int)purpose}|{userId:N}|{expires}|{fingerprint}"));
        return WebEncoders.Base64UrlEncode(_protector.Protect(payload));
    }

    private bool IsValid(string token, TokenPurpose purpose, Guid userId, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(_protector.Unprotect(WebEncoders.Base64UrlDecode(token)));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length != 4
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || DateTimeOffset.FromUnixTimeSeconds(expires) <= clock.GetUtcNow())
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{(int)purpose}|{userId:N}|{fingerprint}"));
        var actual = Encoding.UTF8.GetBytes(parts[0] + "|" + parts[1] + "|" + parts[3]);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string ResetFingerprint(Authenticatable user) => Fingerprint(user.PasswordHash);

    private static string ConfirmFingerprint(Authenticatable user) =>
        Fingerprint(user.Email + "|" + (user.EmailConfirmedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "-"));

    // A hash of the state rather than the state: a token never carries the password hash, even sealed.
    private static string Fingerprint(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)), 0, 12);

    private enum TokenPurpose
    {
        Reset = 1,
        Confirm = 2,
    }
}
