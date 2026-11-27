using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Rask.Wire;

namespace Rask.Auth;

/// <inheritdoc cref="IBearerSignIn" />
/// <typeparam name="TUser">The application's user aggregate.</typeparam>
/// <typeparam name="TKey">Its key: a Guid, or a string holding one.</typeparam>
internal sealed class BearerSignIn<TUser, TKey>(
    AccountService<TUser, TKey> accounts,
    IAuthSessions sessions,
    AuthOptions options,
    IHttpContextAccessor http,
    TimeProvider clock) : IBearerSignIn
    where TUser : Authenticatable<TKey>, new()
    where TKey : notnull
{
    public async Task<BearerIssue> SignIn(
        string name, string password, string? client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(password);

        // Thrown rather than answered: a token nothing can check is a misconfiguration, and Development turns bearer off
        // when the key cannot sign, so an app that asked for it hears why here instead of handing out dead tokens.
        if (!options.Bearer)
        {
            throw new InvalidOperationException(
                "IBearerSignIn issues bearer tokens, and AuthOptions.Bearer is off — set Rask:Auth:Bearer to true and "
                + "Rask:Auth:BearerSigningKey to a key of at least 32 bytes. In Development a missing or short key turns "
                + "bearer off rather than stopping the app.");
        }

        var outcome = await accounts.ValidateAsync(name, password, client, cancellationToken).ConfigureAwait(false);
        if (outcome is not { Result.Succeeded: true, Principal: { } principal }
            || AuthPrincipal.UserId(principal) is not { } userId)
        {
            return new BearerIssue(outcome.Result.Succeeded ? AuthResult.Fail(AuthError.NotAllowed) : outcome.Result, null, null);
        }

        var request = http.HttpContext?.Request;
        var userAgent = request?.Headers.UserAgent.ToString();
        var sessionId = await sessions
            .StartForTokenAsync(
                userId,
                request?.HttpContext.Connection.RemoteIpAddress?.ToString() ?? client,
                string.IsNullOrEmpty(userAgent) ? null : userAgent,
                cancellationToken)
            .ConfigureAwait(false);

        // The session is what the token names: ending it — signing out everywhere, a reset, removing the account — ends the token.
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(AuthPrincipal.SessionClaim, sessionId.ToString()));

        var now = clock.GetUtcNow();
        return new BearerIssue(
            AuthResult.Success,
            BearerTokens.Issue(principal, options, now.UtcDateTime),
            now + options.BearerLifetime);
    }
}
