namespace Rask.Core.Browser;

/// <summary>
///     Attributes for writing a cookie
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Document/cookie" />). Unset members
///     are omitted, taking the browser default.
/// </summary>
public sealed record CookieOptions
{
    /// <summary>Lifetime in seconds (<c>Max-Age</c>). <c>0</c> expires immediately.</summary>
    public int? MaxAgeSeconds { get; init; }

    /// <summary>Absolute expiry (<c>Expires</c>). Sent as an RFC&#160;1123 GMT string.</summary>
    public DateTimeOffset? Expires { get; init; }

    /// <summary>Path scope (<c>Path</c>), e.g. <c>"/"</c>.</summary>
    public string? Path { get; init; }

    /// <summary>Domain scope (<c>Domain</c>).</summary>
    public string? Domain { get; init; }

    /// <summary>Restrict to HTTPS (<c>Secure</c>).</summary>
    public bool Secure { get; init; }

    /// <summary>Cross-site sending policy (<c>SameSite</c>); unset takes the browser default.</summary>
    public SameSiteMode? SameSite { get; init; }
}
