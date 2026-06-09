namespace Rask.Core.Authentication;

/// <summary>
///     Optional, app-facing abstraction over where a bearer token lives on the client for a JWT-based
///     WASM app. The most secure choice is an HttpOnly cookie — the token never reaches JavaScript and no
///     token store is needed. Use this only when a bearer token must be held client-side.
///     <para>
///         The reference implementation (see <c>docs/authentication.md</c>) encrypts the token with ASP.NET
///         Data Protection before writing it to <c>localStorage</c>/<c>sessionStorage</c> via
///         <c>IJSRuntime</c>, so the value at rest is ciphertext — useless if exfiltrated by XSS. A
///         <c>BearerTokenHandler : DelegatingHandler</c> reads it back through this store to attach
///         <c>Authorization: Bearer</c>, and the WASM <c>IUserProvider</c> uses it to hydrate from
///         <c>/api/me</c> and to drive silent refresh. Rask does not consume this interface itself.
///     </para>
/// </summary>
public interface ITokenStore
{
    /// <summary>Return the stored token, or <c>null</c> when none is present.</summary>
    ValueTask<string?> GetAsync();

    /// <summary>
    ///     Persist <paramref name="token" />. When <paramref name="persist" /> is <c>true</c> ("remember me")
    ///     it survives a browser restart; otherwise it is cleared when the tab/session ends.
    /// </summary>
    ValueTask SetAsync(string token, bool persist);

    /// <summary>Remove any stored token (sign-out).</summary>
    ValueTask ClearAsync();
}
