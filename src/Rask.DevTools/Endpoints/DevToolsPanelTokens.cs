using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Rask.DevTools.Endpoints;

/// <summary>
///     The credential a panel page presents for the session it inspects.
/// </summary>
/// <remarks>
///     <para>
///         Derived, not stored: an HMAC-SHA256 of the session id under a key drawn once per process. The session
///         store has no removal event to clean a table from, and a derived token needs none — it is valid for
///         exactly the sessions that exist, and a restart invalidates every one of them, which is right for a tool
///         that only runs while someone is developing.
///     </para>
///     <para>
///         The id alone is not enough, because it is already in the inspected page's markup; the token is what
///         says the panel was opened from that page by the server that rendered it.
///     </para>
/// </remarks>
internal sealed class DevToolsPanelTokens
{
    private const int TokenBytes = 32;

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(TokenBytes);

    /// <summary>The token for <paramref name="sessionId" />, base64url so it travels in a query string as is.</summary>
    internal string For(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        Span<byte> mac = stackalloc byte[TokenBytes];
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(sessionId), mac);
        return Base64Url.EncodeToString(mac);
    }

    /// <summary>Whether <paramref name="token" /> is the token for <paramref name="sessionId" />, in constant time.</summary>
    internal bool Verify(string? sessionId, string? token)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(token))
        {
            return false;
        }

        Span<byte> presented = stackalloc byte[TokenBytes];
        if (!Base64Url.TryDecodeFromChars(token, presented, out var written) || written != TokenBytes)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[TokenBytes];
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(sessionId), expected);
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }
}
