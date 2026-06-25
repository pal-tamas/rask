using System.Globalization;
using Microsoft.JSInterop;

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

/// <summary>
///     Typed access to non-<c>HttpOnly</c> cookies via <c>document.cookie</c>. Inject it through a
///     component constructor and call from an event handler or lifecycle hook:
///     <code>
///     await cookies.SetAsync("theme", "dark", new CookieOptions { MaxAgeSeconds = 31_536_000, Path = "/" });
///     var theme = await cookies.GetAsync("theme");
///     </code>
///     Works on both transports. <c>HttpOnly</c> cookies are invisible to JavaScript by design, so they
///     are neither readable nor writable here — set those from the server.
/// </summary>
public interface ICookies
{
    /// <summary>Reads the value of cookie <paramref name="name" />, or <c>null</c> if absent.</summary>
    ValueTask<string?> GetAsync(string name);

    /// <summary>Writes cookie <paramref name="name" /> with <paramref name="value" /> and optional attributes.</summary>
    ValueTask SetAsync(string name, string value, CookieOptions? options = null);

    /// <summary>
    ///     Deletes cookie <paramref name="name" /> (sets <c>Max-Age=0</c>). Pass the same
    ///     <paramref name="path" /> the cookie was written with, or deletion has no effect.
    /// </summary>
    ValueTask DeleteAsync(string name, string? path = null);

    /// <summary>Reads all visible cookies as a name→value map.</summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync();
}

/// <summary>
///     Default <see cref="ICookies" />, backed by the unified <see cref="IJSRuntime" /> via the
///     framework's <c>__raskApi.cookie*</c> helpers (parsing reads and building the assignment string,
///     which <c>IJSRuntime</c> can't express as a bare property write).
/// </summary>
public sealed class Cookies(IJSRuntime js) : ICookies
{
    /// <inheritdoc />
    public ValueTask<string?> GetAsync(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return js.InvokeAsync<string?>("__raskApi.cookieGet", name);
    }

    /// <inheritdoc />
    public ValueTask SetAsync(string name, string value, CookieOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        // Positional args (maxAge, expires, path, domain, sameSite, secure) — avoids serializing/rooting
        // an options DTO; nulls are skipped by the helper.
        return js.InvokeVoidAsync(
            "__raskApi.cookieSet",
            name,
            value,
            options?.MaxAgeSeconds,
            options?.Expires?.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture),
            options?.Path,
            options?.Domain,
            options?.SameSite?.ToString().ToLowerInvariant(),
            options?.Secure ?? false);
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(string name, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return js.InvokeVoidAsync("__raskApi.cookieDelete", name, path);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync() =>
        await js.InvokeAsync<Dictionary<string, string>>("__raskApi.cookieAll")
        ?? new Dictionary<string, string>();
}
