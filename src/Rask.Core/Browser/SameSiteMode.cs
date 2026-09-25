namespace Rask.Core.Browser;

/// <summary>The <c>SameSite</c> policy for a cookie.</summary>
public enum SameSiteMode
{
    /// <summary>Sent on same-site requests and top-level cross-site navigations.</summary>
    Lax,

    /// <summary>Sent only on same-site requests.</summary>
    Strict,

    /// <summary>Sent on all requests; requires <see cref="CookieOptions.Secure" />.</summary>
    None
}
