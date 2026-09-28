namespace Rask.Wire;

/// <summary>
/// The wire contract for the <c>/api/auth</c> endpoints: the paths, the header, and the shapes.
/// </summary>
/// <remarks>
/// <para>
/// It lives in Rask.Wire because <b>both halves have to agree on it and neither can reference the
/// other</b>. The server half carries Entity Framework, which must never
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

    /// <summary>The route that ends every other session of the caller, relative to the prefix.</summary>
    public const string LogoutOtherDevices = "/logout-other-devices";

    /// <summary>The route that ends every session of the caller, relative to the prefix.</summary>
    public const string LogoutEverywhere = "/logout-everywhere";

    /// <summary>The <c>me</c> route, relative to the prefix.</summary>
    public const string Me = "/me";

    /// <summary>The <c>confirm-email</c> route, relative to the prefix.</summary>
    public const string ConfirmEmail = "/confirm-email";

    /// <summary>The <c>forgot-password</c> route, relative to the prefix.</summary>
    public const string ForgotPassword = "/forgot-password";

    /// <summary>The <c>reset-password</c> route, relative to the prefix.</summary>
    public const string ResetPassword = "/reset-password";

    /// <summary>The route that starts adding a passkey, relative to the prefix.</summary>
    public const string PasskeyRegisterOptions = "/passkeys/register-options";

    /// <summary>The route that finishes adding a passkey, relative to the prefix.</summary>
    public const string PasskeyRegister = "/passkeys/register";

    /// <summary>The route that starts a passkey sign-in, relative to the prefix.</summary>
    public const string PasskeyLoginOptions = "/passkeys/login-options";

    /// <summary>The route that finishes a passkey sign-in, relative to the prefix.</summary>
    public const string PasskeyLogin = "/passkeys/login";

    /// <summary>The route that removes a passkey, relative to the prefix.</summary>
    public const string PasskeyRemove = "/passkeys/remove";
}
