using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Core;

namespace Rask.Auth;

/// <summary>
/// The one-time token that authorises the <b>first</b> registration on an unclaimed instance.
/// </summary>
/// <remarks>
/// <para>
/// A deployed app with an empty user table and an open registration page is a land-grab: whoever
/// reaches it first owns it. The token closes that window without adding a setup wizard — it is
/// generated while the instance is unclaimed, written to the log where the person who deployed the app
/// can see it, and stops mattering the moment an account exists.
/// </para>
/// <para>
/// Only the first registration is gated. Every one after it is an ordinary open registration.
/// </para>
/// </remarks>
public sealed class FirstRunToken
{
    private string? _value;

    /// <summary>
    /// The token, or <c>null</c> once the instance has been claimed (or if it never needed one).
    /// </summary>
    public string? Value => Volatile.Read(ref _value);

    /// <summary>Whether this instance is still waiting to be claimed.</summary>
    public bool IsPending => Value is not null;

    internal void Set(string token) => Volatile.Write(ref _value, token);

    internal void Clear() => Volatile.Write(ref _value, null);

    /// <summary>
    /// Whether <paramref name="candidate"/> is this instance's token, compared in fixed time.
    /// </summary>
    /// <remarks>
    /// Fixed-time because the comparison is a secret check on an unauthenticated endpoint; an ordinary
    /// string comparison leaks the matching prefix length through timing. Returns <c>false</c> when no
    /// token is pending, so a claimed instance cannot be re-claimed.
    /// </remarks>
    public bool Matches(string? candidate)
    {
        var expected = Value;

        if (expected is null || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(candidate));
    }
}

/// <summary>
/// Decides at startup whether this instance still needs a first-run token, and announces it.
/// </summary>
/// <remarks>
/// Runs before the app serves traffic. It asks the database whether the instance has been claimed
/// rather than trusting anything in memory, so a restart of an already-claimed app never re-opens the
/// window, and a restart of an unclaimed one issues a token that is actually usable.
/// </remarks>
internal sealed partial class FirstRunTokenInitializer(
    IInstanceClaimStore claims,
    FirstRunToken token,
    AuthOptions options,
    ILogger<FirstRunTokenInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.FirstUserIsAdmin || !options.RequireFirstRunToken)
        {
            return;
        }

        // The database is the authority on "has anybody claimed this yet", not a flag in memory.
        bool claimed;

        try
        {
            claimed = await claims.IsClaimedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A freshly scaffolded app starts before its first migration has run, so the table may not
            // exist yet. Refusing to boot over that would make "auth is on by default" mean "a new app
            // does not start". Fail closed instead: an instance we cannot prove is claimed is treated
            // as unclaimed, so the first registration is still gated by a token.
            ClaimStateUnknown(logger, ex);
            claimed = false;
        }

        if (claimed)
        {
            return;
        }

        var value = string.IsNullOrWhiteSpace(options.FirstRunToken)
            ? SecureToken.Create()
            : options.FirstRunToken;

        token.Set(value);
        FirstRunTokenIssued(logger, options.RegisterPath, value);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Could not read whether this app has been claimed yet — treating it as unclaimed, so "
                  + "the first registration still needs a token. This is expected before the first "
                  + "migration has run.")]
    private static partial void ClaimStateUnknown(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "This Rask app has no accounts yet. The first registration claims it and becomes the "
                  + "administrator, and needs this one-time token: {Token}. Claim it at {RegisterPath}. "
                  + "The token stops working as soon as an account exists.")]
    private static partial void FirstRunTokenIssued(ILogger logger, string registerPath, string token);
}
