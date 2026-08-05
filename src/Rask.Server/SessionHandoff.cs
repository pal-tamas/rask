using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Rask.Server;

/// <summary>What a host needs to rebuild a page it has never seen: where it was, and what the app declared.</summary>
/// <param name="Url">The path + query the session was on, in the shape <c>SplitUrl</c> parses.</param>
/// <param name="Entries">The <see cref="Rask.Core.Live.IPersistentState" /> bag, still as UTF-8 JSON.</param>
internal sealed record SessionHandoffRecord(string Url, IReadOnlyList<KeyValuePair<string, byte[]>> Entries);

/// <summary>
///     Whether this host can seal and open resume records, resolved once at startup.
/// </summary>
/// <remarks>
///     A holder rather than a nullable registration: a DI factory that returns <c>null</c> throws on
///     resolve, and inspecting the service collection for <c>IDataProtectionProvider</c> would make the
///     outcome depend on whether <c>AddRask</c> ran before or after the host registered it. This is always
///     resolvable and carries the answer inside, so the endpoint asks once and branches.
/// </remarks>
internal sealed class SessionResumeSupport
{
    internal SessionResumeSupport(SessionHandoffProtector? protector) => Protector = protector;

    /// <summary>The protector, or <c>null</c> when resume is off or the host has no Data Protection.</summary>
    internal SessionHandoffProtector? Protector { get; }

    internal bool Enabled => Protector is not null;
}

/// <summary>Why a resume record was refused. Rides the <c>rask.sessions.resume_rejected</c> metric as a tag.</summary>
internal enum ResumeRejection
{
    None,
    Malformed,
    Unprotect,
    Principal,
    TooLarge,
    AtCapacity
}

/// <summary>
///     Seals and opens the record a client carries between one live session and the next.
/// </summary>
/// <remarks>
///     <para>
///         The record goes to the browser, so it is encrypted and authenticated with ASP.NET Data
///         Protection under its own purpose — an app's other protected payloads can neither read it nor be
///         read by it. Expiry is delegated to <see cref="ITimeLimitedDataProtector" /> rather than carried
///         as a field we remember to check: an expired record then fails to open at all, instead of relying
///         on a comparison at one call site.
///     </para>
///     <para>
///         <b>The record is not a credential and must never become one.</b> It carries no principal and no
///         claims; a reconnect authenticates exactly as it does today, from the cookie or bearer token on
///         the WebSocket handshake. What the record does carry is the identity it was <em>issued</em> to,
///         as a keyed hash — so a record cannot be replayed onto a different account, and signing out and
///         back in as someone else cannot inherit the previous user's page. The hash is keyed by the data
///         protector itself, so the stored value discloses nothing about the user.
///     </para>
///     <para>
///         Payload is a length-prefixed binary frame rather than JSON: the bag's values are already UTF-8
///         JSON, and nesting them in an outer JSON document would mean base64-ing every one of them —
///         about a third more bytes on a wire we are trying to keep cheap.
///     </para>
/// </remarks>
internal sealed class SessionHandoffProtector
{
    /// <summary>Data-protection purpose. Versioned: changing the payload shape means changing this string.</summary>
    internal const string Purpose = "Rask.LiveSession.Resume.v1";

    /// <summary>
    ///     Refuse an oversized token before spending anything on it. Base64 of a protected 16 KB bag plus
    ///     its URL lands well under this; the cap exists so a client cannot make us allocate megabytes by
    ///     sending a long string that was never going to open.
    /// </summary>
    internal const int MaxTokenChars = 64 * 1024;

    private const byte FormatVersion = 1;

    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeSpan _lifetime;

    internal SessionHandoffProtector(IDataProtectionProvider provider, TimeSpan lifetime)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        _lifetime = lifetime;
    }

    /// <summary>
    ///     Seals a record for <paramref name="user" />. The returned string is safe to hand to the browser.
    /// </summary>
    internal string Protect(string url, ClaimsPrincipal? user, IReadOnlyDictionary<string, byte[]> entries)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(FormatVersion);
            writer.Write(url);
            var binding = UserBinding(user);
            writer.Write(binding.Length);
            writer.Write(binding);
            writer.Write(entries.Count);
            foreach (var (key, value) in entries)
            {
                writer.Write(key);
                writer.Write(value.Length);
                writer.Write(value);
            }
        }

        return Convert.ToBase64String(_protector.Protect(buffer.ToArray(), _lifetime));
    }

    /// <summary>
    ///     Opens a record and checks it belongs to <paramref name="user" />. Returns <c>false</c> with a
    ///     reason for every failure — nothing here throws at the caller, because every input is attacker-
    ///     controlled and a refusal is an ordinary outcome, not an error.
    /// </summary>
    internal bool TryUnprotect(
        string token,
        ClaimsPrincipal? user,
        out SessionHandoffRecord? record,
        out ResumeRejection rejection)
    {
        record = null;

        if (token.Length > MaxTokenChars)
        {
            rejection = ResumeRejection.TooLarge;
            return false;
        }

        byte[] plaintext;
        try
        {
            plaintext = _protector.Unprotect(Convert.FromBase64String(token));
        }
        catch (FormatException)
        {
            // Not base64 at all.
            rejection = ResumeRejection.Malformed;
            return false;
        }
        catch (CryptographicException)
        {
            // Tampered, expired, or sealed under a key ring this host doesn't have. Deliberately one
            // outcome: telling a client which of those it was is telling an attacker how close they got.
            rejection = ResumeRejection.Unprotect;
            return false;
        }

        try
        {
            using var buffer = new MemoryStream(plaintext, writable: false);
            using var reader = new BinaryReader(buffer, Encoding.UTF8, leaveOpen: true);

            if (reader.ReadByte() != FormatVersion)
            {
                // A record written by a different shape of this code. Nothing to salvage: refuse and let
                // the client reload, which is what it would have done before any of this existed.
                rejection = ResumeRejection.Malformed;
                return false;
            }

            var url = reader.ReadString();

            var bindingLength = reader.ReadInt32();
            if (bindingLength is < 0 or > 64)
            {
                rejection = ResumeRejection.Malformed;
                return false;
            }

            var boundUser = reader.ReadBytes(bindingLength);
            if (boundUser.Length != bindingLength)
            {
                rejection = ResumeRejection.Malformed;
                return false;
            }

            // FixedTimeEquals handles the length-mismatch case (anonymous record vs authenticated
            // reconnect, and the reverse) as a plain false rather than an early return that leaks which.
            if (!CryptographicOperations.FixedTimeEquals(boundUser, UserBinding(user)))
            {
                rejection = ResumeRejection.Principal;
                return false;
            }

            var count = reader.ReadInt32();
            if (count < 0)
            {
                rejection = ResumeRejection.Malformed;
                return false;
            }

            var entries = new List<KeyValuePair<string, byte[]>>(Math.Min(count, 64));
            for (var i = 0; i < count; i++)
            {
                var key = reader.ReadString();
                var length = reader.ReadInt32();
                if (length < 0)
                {
                    rejection = ResumeRejection.Malformed;
                    return false;
                }

                // ReadBytes returns short rather than throwing at EOF, so a truncated record would
                // otherwise restore silently-empty values.
                var value = reader.ReadBytes(length);
                if (value.Length != length)
                {
                    rejection = ResumeRejection.Malformed;
                    return false;
                }

                entries.Add(new KeyValuePair<string, byte[]>(key, value));
            }

            record = new SessionHandoffRecord(url, entries);
            rejection = ResumeRejection.None;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or OutOfMemoryException)
        {
            // The plaintext opened, so it was written by this app under this key ring — a shape it can't
            // parse means a bug or a format skew, not an attack. Refuse the same way regardless.
            rejection = ResumeRejection.Malformed;
            return false;
        }
    }

    /// <summary>
    ///     A stable, non-reversible stand-in for "who this record was issued to". Anonymous sessions bind
    ///     to an empty binding, so an anonymous record can be redeemed only while still anonymous — signing
    ///     in mid-session invalidates it, which is the safe direction.
    /// </summary>
    /// <remarks>
    ///     A plain hash rather than anything keyed, for one hard reason: the binding must come out the same
    ///     on a <em>different process</em> from the one that issued it, which is the entire point of the
    ///     record. Data Protection is deliberately non-deterministic — protecting the same id twice gives
    ///     different bytes — so it cannot be used to derive this, and a per-process key would refuse every
    ///     record that crossed a restart. The hash is safe here because it never leaves the encrypted
    ///     envelope: reading it already requires the key ring, and anyone holding that can forge records
    ///     outright. Domain-separated so it can't be confused with a hash the app computes elsewhere.
    /// </remarks>
    private static byte[] UserBinding(ClaimsPrincipal? user)
    {
        var id = user?.Identity?.IsAuthenticated == true
            ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity.Name
            : null;

        return string.IsNullOrEmpty(id)
            ? []
            : SHA256.HashData(Encoding.UTF8.GetBytes("Rask.Resume.User " + id));
    }
}
