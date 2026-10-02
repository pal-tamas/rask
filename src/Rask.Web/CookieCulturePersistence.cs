using System.Globalization;
using Rask.Core.Forms;
using Rask.Core.Globalization;

namespace Rask.Web;

/// <summary>
///     Remembers the chosen culture in a cookie, written through <c>document.cookie</c>.
/// </summary>
/// <remarks>
///     <para>
///         <c>document.cookie</c> works on both hosts and over plain HTTP, which the async, HTTPS-only
///         <c>cookieStore</c> does not. The cookie is written exactly as before it moved here, so a visitor's
///         existing one keeps working.
///     </para>
///     <para>
///         The new value reaches the <em>server</em> only on the next HTTP request. That is not a gap: the session
///         switched the moment <see cref="IRaskCulture.SetAsync(string)" /> returned, and the cookie exists to survive
///         a reload, not to carry the current render.
///     </para>
/// </remarks>
public sealed class CookieCulturePersistence(IServiceProvider services, RaskCultureOptions options)
    : IRaskCulturePersistence
{
    /// <inheritdoc />
    public async Task SaveAsync(string culture, string uiCulture, CancellationToken cancellationToken = default)
    {
        // The session's own services, so the write reaches this visitor's page even when the switch was not
        // made from an event handler — a service holding IRaskCulture, say.
        using (DispatchServicesScope.Push(services))
        {
            await Document.SetCookie(Cookie(culture, uiCulture)).ConfigureAwait(false);
        }
    }

    // Lax, not Strict: a visitor following a link into the app from anywhere else should still arrive in their own
    // language. The value is a language tag, never a credential.
    private string Cookie(string culture, string uiCulture)
    {
        var name = Uri.EscapeDataString(options.CookieName);
        var value = Uri.EscapeDataString(RaskCultureCookie.Format(culture, uiCulture));
        var maxAge = options.CookieMaxAgeDays * 24 * 60 * 60;
        return string.Create(CultureInfo.InvariantCulture, $"{name}={value}; max-age={maxAge}; path=/; samesite=lax");
    }
}
