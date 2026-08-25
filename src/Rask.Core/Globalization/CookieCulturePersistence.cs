using Rask.Core.Browser;

namespace Rask.Core.Globalization;

/// <summary>
///     Remembers the chosen culture in a cookie, written through the browser.
/// </summary>
/// <remarks>
///     Uses <see cref="ICookies" />, so it works identically on the server (the write rides the live
///     socket with the next render payload) and in WASM (it happens in-process). One consequence worth
///     knowing: because this goes through <c>document.cookie</c>, the new value reaches the
///     <em>server</em> only on the next HTTP request. That is not a gap — the session switched the
///     moment <see cref="IRaskCulture.SetAsync(string)" /> returned, and the cookie exists to survive a
///     reload, not to carry the current render.
/// </remarks>
public sealed class CookieCulturePersistence(ICookies cookies, RaskCultureOptions options)
    : IRaskCulturePersistence
{
    /// <inheritdoc />
    public Task SaveAsync(string culture, string uiCulture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = RaskCultureCookie.Format(culture, uiCulture);
        return cookies.SetAsync(
            options.CookieName,
            value,
            new CookieOptions
            {
                MaxAgeSeconds = options.CookieMaxAgeDays * 24 * 60 * 60,
                Path = "/",
                // Lax, not Strict: a visitor following a link into the app from anywhere else should
                // still arrive in their own language. The value is a language tag, never a credential.
                SameSite = SameSiteMode.Lax,
            }).AsTask();
    }
}

/// <summary>
///     Remembers nothing. The default where there is nowhere to write, and what an app gets when it
///     turns <see cref="RaskCultureOptions.UseCookie" /> off.
/// </summary>
public sealed class NullCulturePersistence : IRaskCulturePersistence
{
    /// <inheritdoc />
    public Task SaveAsync(string culture, string uiCulture, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
