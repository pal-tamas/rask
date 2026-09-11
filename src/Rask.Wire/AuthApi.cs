namespace Rask.Wire;

/// <summary>
/// The wire contract for the <c>/api/auth</c> endpoints: the paths, the header, and the shapes.
/// </summary>
/// <remarks>
/// <para>
/// It lives in Rask.Wire because <b>both halves have to agree on it and neither can reference the
/// other</b>. The server half carries ASP.NET Core Identity and Entity Framework, which must never
/// reach a trimmed WebAssembly publish; the browser half carries an <c>HttpClient</c> and nothing else.
/// Rask.Wire is the one package both can take — zero dependencies, trimming-clean, and already the home
/// of the carriers Rask.Cqrs and Rask.Api share for exactly this reason — so the contract is written
/// once rather than duplicated and left to drift.
/// </para>
/// <para>
/// It was in Rask.Core until the accounts battery had to work on a host that has no Rask component
/// runtime at all (#1069). Core was never the right home on the merits: it uses none of this, and
/// putting the contract there made every consumer of the contract a consumer of the renderer.
/// </para>
/// <para>
/// A TypeScript front end speaks the same four routes. This type is what keeps the C# clients and that
/// front end describing one API rather than three.
/// </para>
/// </remarks>
public static class AuthApi
{
    /// <summary>The default path the endpoints sit under.</summary>
    public const string DefaultPrefix = "/api/auth";

    /// <summary>The header every state-changing auth request must carry.</summary>
    /// <remarks>
    /// A custom header is a CSRF defence that needs no token round-trip: cross-site markup — a form,
    /// an <c>&lt;img&gt;</c>, a <c>&lt;script&gt;</c> — cannot set one, so only a same-origin
    /// <c>fetch</c> reaches these endpoints. It layers over the <c>SameSite=Lax</c> cookie, which
    /// already withholds itself from a cross-site POST; two cheap defences are worth more than one on
    /// the endpoint that mints a session.
    /// </remarks>
    public const string RequestHeader = "X-Rask-Auth";

    /// <summary>
    ///     Asks a sign-in to answer with a bearer token instead of relying on the cookie.
    /// </summary>
    /// <remarks>
    ///     Send <c>X-Rask-Auth-Mode: bearer</c> alongside <see cref="RequestHeader" />. A header rather
    ///     than a body field, so every endpoint that completes a sign-in answers the same way without
    ///     each request type growing a flag of its own — and a client sets it once, next to the header it
    ///     already has to send. Ignored unless the app turned <c>AuthOptions.Bearer</c> on, in which case
    ///     the ordinary cookie answer comes back: the caller IS signed in, and saying otherwise would be
    ///     a lie.
    /// </remarks>
    public const string AuthModeHeader = "X-Rask-Auth-Mode";

    /// <summary>The one value <see cref="AuthModeHeader" /> takes.</summary>
    public const string BearerMode = "bearer";

    /// <summary>The <c>register</c> route, relative to the prefix.</summary>
    public const string Register = "/register";

    /// <summary>The <c>login</c> route, relative to the prefix.</summary>
    public const string Login = "/login";

    /// <summary>The <c>logout</c> route, relative to the prefix.</summary>
    public const string Logout = "/logout";

    /// <summary>The <c>me</c> route, relative to the prefix.</summary>
    public const string Me = "/me";

    /// <summary>The <c>confirm-email</c> route, relative to the prefix.</summary>
    public const string ConfirmEmail = "/confirm-email";

    /// <summary>The <c>forgot-password</c> route, relative to the prefix.</summary>
    public const string ForgotPassword = "/forgot-password";

    /// <summary>The <c>reset-password</c> route, relative to the prefix.</summary>
    public const string ResetPassword = "/reset-password";
}

/// <summary>Credentials for a new account.</summary>
/// <param name="Email">The email address, which is also the user name.</param>
/// <param name="Password">The password.</param>
/// <param name="FirstRunToken">The first-run token, needed only while no account exists yet.</param>
public sealed record RegisterRequest(string Email, string Password, string? FirstRunToken = null);

/// <summary>Credentials for an existing account.</summary>
/// <param name="Email">The email address.</param>
/// <param name="Password">The password.</param>
/// <param name="Remember">Whether the session should outlive the browser session.</param>
public sealed record LoginRequest(string Email, string Password, bool Remember = false);

/// <summary>An address to send a password-reset link to.</summary>
/// <param name="Email">The email address.</param>
public sealed record ForgotPasswordRequest(string Email);

/// <summary>A new password, and the emailed token that authorizes setting it.</summary>
/// <param name="UserId">The account the link named.</param>
/// <param name="Token">The token the link carried.</param>
/// <param name="Password">The new password.</param>
public sealed record ResetPasswordRequest(string UserId, string Token, string Password);

/// <summary>An address to mark confirmed, and the emailed token that proves it.</summary>
/// <param name="UserId">The account the link named.</param>
/// <param name="Token">The token the link carried.</param>
public sealed record ConfirmEmailRequest(string UserId, string Token);

/// <summary>Who is signed in.</summary>
/// <param name="Id">The account's stable id.</param>
/// <param name="Email">The account's email address.</param>
/// <param name="Roles">The roles it holds.</param>
public sealed record CurrentUser(string? Id, string? Email, IReadOnlyList<string> Roles);

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

/// <summary>Why a request was refused.</summary>
/// <param name="Error">The <c>AuthError</c> name — the enum lives in Rask.Core, which this
/// contract deliberately does not reference, so it travels as its name.</param>
/// <param name="Message">A human-readable detail, when there is one.</param>
public sealed record AuthFailure(string Error, string? Message);
