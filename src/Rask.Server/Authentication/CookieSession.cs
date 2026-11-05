using Microsoft.AspNetCore.Http;

namespace Rask.Server.Authentication;

/// <summary>
///     Writes and clears the session cookie a sign-in with the app's own token travels in. The flags are fixed: script
///     never reads it (<c>HttpOnly</c>), it never crosses plain HTTP (<c>Secure</c>), and another site's request never
///     carries it but a link followed here (<c>SameSite=Lax</c>).
/// </summary>
internal sealed class CookieSession(CookieSessionOptions options, TimeProvider clock)
{
    public string Name => options.Name;

    public void Write(HttpResponse response, string token)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrEmpty(token);
        var expires = options.Lifetime is { } lifetime ? clock.GetUtcNow() + lifetime : (DateTimeOffset?)null;
        response.Cookies.Append(options.Name, token, Flags(expires));
    }

    // Deleted with the Domain and Path it was written with: a cookie is only replaced by one that matches both.
    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(options.Name, Flags(expires: null));
    }

    private CookieOptions Flags(DateTimeOffset? expires) => new()
    {
        Path = "/",
        Domain = options.Domain,
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Expires = expires,
    };
}
