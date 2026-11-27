namespace Rask.Auth;

/// <summary>
/// Checks a name and a password and issues a bearer token for them, for an app that keeps a sign-in contract of its own
/// — a partner's <c>POST /api/auth</c> that answers the token as plain text — while Rask does the work.
/// </summary>
/// <remarks>
/// <para>
/// The same checks as <c>POST /api/auth/login</c>: throttled per name and client, an unknown name costs a password check
/// anyway so the answer says nothing about which names exist, a hash in an older format (a bcrypt one carried over) is
/// renewed on success, and a disabled or unconfirmed account is refused. A success starts a <see cref="Session" /> row the
/// token names, so signing the user out everywhere, a password reset or removing the account ends the token too.
/// </para>
/// <para>
/// The session lasts <see cref="AuthOptions.BearerLifetime" />, the token's own lifetime, and is not extended by using the
/// token: a partner that signs in every few minutes leaves rows that expire with their tokens and are swept a day later,
/// rather than one per sign-in living as long as a cookie's.
/// </para>
/// <example>
/// <code>
/// app.MapPost("/api/auth", async (Credentials body, HttpContext http, IBearerSignIn bearer) =&gt;
///     await bearer.SignIn(body.Username, body.Password, AuthClient.Of(http)) is { Token: { } token }
///         ? Results.Text(token)
///         : Results.Unauthorized());
/// </code>
/// </example>
/// </remarks>
public interface IBearerSignIn
{
    /// <summary>Checks <paramref name="name" /> and <paramref name="password" />, and issues a token when they match.</summary>
    /// <param name="name">What the account signs in with: its address, or its <see cref="SignInNameAttribute">[SignInName]</see>.</param>
    /// <param name="password">The password.</param>
    /// <param name="client">The client the attempt is counted against — <see cref="AuthClient.Of" /> of the request.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The token and when it expires, or why not.</returns>
    /// <exception cref="InvalidOperationException"><see cref="AuthOptions.Bearer" /> is off, so no token can be checked.</exception>
    Task<BearerIssue> SignIn(string name, string password, string? client, CancellationToken cancellationToken = default);
}
