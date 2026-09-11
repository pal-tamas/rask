using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Rask.Storage.Serving;

/// <summary>
/// Seals and opens the token in a temporary URL served by the app (<c>/_rask/files/{token}</c>).
/// </summary>
/// <remarks>
/// <para>
/// Modelled on the live session's resume record: ASP.NET Data Protection under a purpose of its own, so no
/// other protected payload in the app can be replayed as one of these, and expiry enforced by
/// <see cref="ITimeLimitedDataProtector"/> — an expired token fails to open at all rather than relying on a
/// comparison somebody remembers to make.
/// </para>
/// <para>
/// The payload is the file id and a format version, nothing else: no name, no key, no user. What a token can
/// reach is decided when it is presented, from the row — so deleting a file revokes every link to it.
/// </para>
/// </remarks>
internal sealed class TemporaryUrlProtector
{
    /// <summary>Versioned: changing the payload means changing this string.</summary>
    internal const string Purpose = "Rask.Storage.TemporaryUrl.v1";

    /// <summary>A real token is about 140 characters. Anything past this is refused before it is decoded.</summary>
    internal const int MaxTokenChars = 256;

    private const byte FormatVersion = 1;
    private const int PayloadLength = 17;

    private readonly ITimeLimitedDataProtector _protector;

    internal TemporaryUrlProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    internal string Protect(Guid id, TimeSpan lifetime)
    {
        var payload = new byte[PayloadLength];
        payload[0] = FormatVersion;
        id.TryWriteBytes(payload.AsSpan(1));
        return Base64Url.EncodeToString(_protector.Protect(payload, lifetime));
    }

    /// <summary>Never throws: every token is attacker-controlled, and a refusal is an ordinary answer.</summary>
    internal bool TryUnprotect(string? token, out Guid id)
    {
        id = default;
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenChars)
        {
            return false;
        }

        byte[] sealedBytes;
        try
        {
            sealedBytes = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] payload;
        try
        {
            payload = _protector.Unprotect(sealedBytes, out _);
        }
        catch (CryptographicException)
        {
            // Tampered, expired, or sealed under a key ring this app no longer has — indistinguishable on purpose.
            return false;
        }

        if (payload.Length != PayloadLength || payload[0] != FormatVersion)
        {
            return false;
        }

        id = new Guid(payload.AsSpan(1));
        return true;
    }
}
