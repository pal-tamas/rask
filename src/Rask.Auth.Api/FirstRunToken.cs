using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// SecureToken, source-linked from Rask.Core (see the csproj) and keeping its namespace there.
using Rask.Core;

namespace Rask.Auth;

/// <summary>
/// The one-time token that authorises the <b>first</b> registration on an unclaimed instance.
/// </summary>
/// <remarks>
/// <para>
/// A deployed app with an empty user table and an open registration page is a land-grab: whoever
/// reaches it first owns it. The token closes that window without adding a setup wizard — it is
/// generated while the instance is unclaimed, written to the log where the person who deployed the app
/// can see it, and stops mattering the moment an account exists.
/// </para>
/// <para>
/// Only the first registration is gated. Every one after it is an ordinary open registration.
/// </para>
/// </remarks>
public sealed class FirstRunToken
{
    private string? _value;

    /// <summary>
    /// The token, or <c>null</c> once the instance has been claimed (or if it never needed one).
    /// </summary>
    public string? Value => Volatile.Read(ref _value);

    /// <summary>Whether this instance is still waiting to be claimed.</summary>
    public bool IsPending => Value is not null;

    internal void Set(string token) => Volatile.Write(ref _value, token);

    internal void Clear() => Volatile.Write(ref _value, null);

    /// <summary>
    /// Whether <paramref name="candidate"/> is this instance's token, compared in fixed time.
    /// </summary>
    /// <remarks>
    /// Fixed-time because the comparison is a secret check on an unauthenticated endpoint; an ordinary
    /// string comparison leaks the matching prefix length through timing. Returns <c>false</c> when no
    /// token is pending, so a claimed instance cannot be re-claimed.
    /// </remarks>
    public bool Matches(string? candidate)
    {
        var expected = Value;

        if (expected is null || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(candidate));
    }
}
