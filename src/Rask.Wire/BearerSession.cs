namespace Rask.Wire;

/// <summary>
///     What a sign-in answers when the caller asked for a bearer token.
/// </summary>
/// <param name="AccessToken">The token, to be sent as <c>Authorization: Bearer …</c>.</param>
/// <param name="TokenType">Always <c>Bearer</c>. Present so a generic client need not assume.</param>
/// <param name="ExpiresIn">Seconds the token is good for.</param>
/// <param name="User">Who was signed in, exactly as <c>/me</c> would describe them.</param>
/// <remarks>
///     <para>
///         There is no refresh token, and that is a decision rather than an omission: refresh needs a
///         revocation story, revocation needs storage, and that is a much larger feature. When the token
///         expires the caller signs in again.
///     </para>
///     <para>
///         The token is in the body only — never a cookie, never a response header — so nothing stores
///         it on the caller's behalf. A token in browser storage is XSS-readable, which is precisely why
///         the cookie path exists and stays the default for anything running in a page.
///     </para>
/// </remarks>
public sealed record BearerSession(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    CurrentUser User);
