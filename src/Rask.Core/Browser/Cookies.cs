using System.Globalization;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
        ?? new Dictionary<string, string>(StringComparer.Ordinal);
}
