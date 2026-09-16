using System.Reflection;

namespace Rask.Auth;

/// <summary>
/// Which site a passkey belongs to: the relying party id it is bound to, and the origins that may use it.
/// </summary>
/// <remarks>
/// <para>
/// An app configures none of this and it still works, because the request already says where the app is. The order is
/// the same everywhere: what the app configured, then <c>PublicOrigin</c>, then the origin the ceremony came from. The
/// last one is safe to trust here and only here — the browser writes the origin into the client data itself, so what
/// this resolves is compared against what the browser said, not taken from a header the caller controls.
/// </para>
/// <para>
/// An app behind a proxy, or serving several subdomains, sets <c>PasskeyRelyingPartyId</c> and <c>PasskeyOrigins</c>
/// and stops depending on the request at all.
/// </para>
/// </remarks>
internal sealed class PasskeySite(AuthOptions options)
{
    /// <summary>The site name the platform's passkey dialog shows.</summary>
    public string Name =>
        options.PasskeyRelyingPartyName
        ?? Assembly.GetEntryAssembly()?.GetName().Name
        ?? "Rask";

    /// <summary>The origins a ceremony may come from, given the origin this request arrived on.</summary>
    /// <param name="requestOrigin">The origin of the request completing the ceremony, when it is known.</param>
    public IReadOnlyList<string> Origins(string? requestOrigin)
    {
        if (options.PasskeyOrigins.Count > 0)
        {
            return [.. options.PasskeyOrigins.Select(Normalize).Where(o => o is not null).Select(o => o!)];
        }

        return (Normalize(options.PublicOrigin) ?? Normalize(requestOrigin)) is { } origin ? [origin] : [];
    }

    /// <summary>The relying party id, given the origin this request arrived on.</summary>
    /// <param name="requestOrigin">The origin of the request completing the ceremony, when it is known.</param>
    public string RelyingPartyId(string? requestOrigin)
    {
        if (options.PasskeyRelyingPartyId is { Length: > 0 } configured)
        {
            return configured;
        }

        // The host, never the origin: an RP id carries no scheme and no port, and hashing one that did would produce a
        // value no browser can match.
        var from = options.PublicOrigin ?? requestOrigin;
        return Uri.TryCreate(from, UriKind.Absolute, out var uri) ? uri.Host : "localhost";
    }

    /// <summary>Everything a ceremony is verified against.</summary>
    /// <param name="requestOrigin">The origin of the request completing the ceremony, when it is known.</param>
    /// <param name="challenge">The challenge that was issued.</param>
    public PasskeyCeremony Ceremony(string? requestOrigin, byte[] challenge) =>
        new(RelyingPartyId(requestOrigin), Origins(requestOrigin), challenge);

    // A browser writes its origin as scheme://host[:non-default-port]. Uri produces exactly that, so a configured
    // origin with a trailing slash or a path still compares equal to what arrives.
    private static string? Normalize(string? origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;
}
