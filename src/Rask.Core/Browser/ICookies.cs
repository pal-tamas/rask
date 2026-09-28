namespace Rask.Core.Browser;

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
