namespace Rask.Core.Authentication;

/// <summary>
/// The wire contract for the <c>/api/auth</c> endpoints: the paths, the header, and the shapes.
/// </summary>
/// <remarks>
/// <para>
/// It lives in Core because <b>both halves have to agree on it and neither can reference the other</b>.
/// The server half carries ASP.NET Core Identity and Entity Framework, which must never reach a
/// trimmed WebAssembly publish; the browser half carries an <c>HttpClient</c> and nothing else. Core is
/// the one assembly they share, so the contract is written once rather than duplicated and left to
/// drift.
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

    /// <summary>The <c>register</c> route, relative to the prefix.</summary>
    public const string Register = "/register";

    /// <summary>The <c>login</c> route, relative to the prefix.</summary>
    public const string Login = "/login";

    /// <summary>The <c>logout</c> route, relative to the prefix.</summary>
    public const string Logout = "/logout";

    /// <summary>The <c>me</c> route, relative to the prefix.</summary>
    public const string Me = "/me";
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

/// <summary>Who is signed in.</summary>
/// <param name="Id">The account's stable id.</param>
/// <param name="Email">The account's email address.</param>
/// <param name="Roles">The roles it holds.</param>
public sealed record CurrentUser(string? Id, string? Email, IReadOnlyList<string> Roles);

/// <summary>Why a request was refused.</summary>
/// <param name="Error">The <see cref="AuthError" /> name.</param>
/// <param name="Message">A human-readable detail, when there is one.</param>
public sealed record AuthFailure(string Error, string? Message);
