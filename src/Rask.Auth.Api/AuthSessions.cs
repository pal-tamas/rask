using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Auth;

/// <summary>Starts, resumes and ends <see cref="Session" /> rows, without naming the user or context type.</summary>
internal interface IAuthSessions
{
    /// <summary>Starts a session for <paramref name="userId" />, returning its id.</summary>
    Task<Guid> StartAsync(
        Guid userId, string? ipAddress, string? userAgent, bool persistent, CancellationToken cancellationToken = default);

    /// <summary>
    /// The principal for a live session, freshly built from its user, or <see langword="null" /> when the session has
    /// ended, expired, or its user is gone.
    /// </summary>
    Task<ClaimsPrincipal?> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Ends one session.</summary>
    Task EndAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Ends every session of <paramref name="userId" />, except <paramref name="except" />.</summary>
    Task<int> EndAllAsync(Guid userId, Guid? except = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAuthSessions" />
/// <remarks>
/// A resumed session is cached for up to 30 seconds per process, so a busy page costs one read every half minute rather
/// than one per request. Ending a session removes it from this process's cache at once; another replica stops honouring it
/// within that window.
/// </remarks>
internal sealed class AuthSessions<TContext, TUser>(
    IDbContextFactory<TContext> contexts,
    IMemoryCache cache,
    TimeProvider clock,
    AuthOptions options) : IAuthSessions
    where TContext : DbContext
    where TUser : Authenticatable
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public async Task<Guid> StartAsync(
        Guid userId, string? ipAddress, string? userAgent, bool persistent, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var session = Session.Start(userId, ipAddress, userAgent, persistent, now, options.ExpireTimeSpan);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Add(session);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return session.Id;
    }

    public async Task<ClaimsPrincipal?> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        if (cache.TryGetValue(CacheKey(sessionId), out Snapshot? cached) && cached is not null)
        {
            if (cached.ExpiresAt > now)
            {
                return AuthPrincipal.For(cached.UserId, cached.Email, cached.Roles, sessionId);
            }

            cache.Remove(CacheKey(sessionId));
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Both query filters apply: an ended (soft-deleted) session and a deleted user are not found.
        var found = await (
                from s in db.Set<Session>().AsNoTracking()
                join u in db.Set<TUser>().AsNoTracking() on s.UserId equals u.Id
                where s.Id == sessionId
                select new { s.UserId, u.Email, u.Roles, s.ExpiresAt, s.LastSeenAt })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (found is null || found.ExpiresAt <= now)
        {
            return null;
        }

        var expiresAt = found.ExpiresAt;

        // At most once a minute: a write per request would make every page view a database write.
        if (now - found.LastSeenAt >= TimeSpan.FromMinutes(1))
        {
            expiresAt = options.SlidingExpiration ? now + options.ExpireTimeSpan : found.ExpiresAt;
            await db.Set<Session>()
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(s => s.LastSeenAt, now).SetProperty(s => s.ExpiresAt, expiresAt),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var snapshot = new Snapshot(found.UserId, found.Email, [.. found.Roles], expiresAt);
        cache.Set(CacheKey(sessionId), snapshot, CacheFor);

        return AuthPrincipal.For(snapshot.UserId, snapshot.Email, snapshot.Roles, sessionId);
    }

    public async Task EndAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cache.Remove(CacheKey(sessionId));

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (await db.Set<Session>().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false)
            is not { } session)
        {
            return;
        }

        session.End(clock.GetUtcNow().UtcDateTime);
        db.Remove(session);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> EndAllAsync(Guid userId, Guid? except = null, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var sessions = await db.Set<Session>()
            .Where(s => s.UserId == userId && s.Id != except)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var session in sessions)
        {
            cache.Remove(CacheKey(session.Id));
            session.End(now);
            db.Remove(session);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return sessions.Count;
    }

    private static string CacheKey(Guid sessionId) => "Rask.Auth.Session:" + sessionId.ToString("N");

    private sealed record Snapshot(Guid UserId, string Email, string[] Roles, DateTime ExpiresAt);
}

/// <summary>
/// The cookie handler's side of sessions: a sign-in starts a row, a sign-out ends it, and every request resumes it.
/// </summary>
/// <remarks>
/// Every sign-in on every host ends in <c>HttpContext.SignInAsync</c> on the cookie scheme: the <c>/api/auth</c> endpoints
/// call it, and the Server host's ticket redeem calls it. So this is the one place that has to know about rows, and the
/// cookie itself carries only the session id beside the claims it is rebuilt from.
/// </remarks>
internal sealed class AuthCookieEvents(IAuthSessions sessions) : CookieAuthenticationEvents
{
    public override async Task SigningIn(CookieSigningInContext context)
    {
        // A principal that already names a session (a renewal), or that is not one of ours, is left as it is.
        if (context.Principal?.Identity is not ClaimsIdentity identity
            || AuthPrincipal.SessionId(context.Principal) is not null
            || AuthPrincipal.UserId(context.Principal) is not { } userId)
        {
            return;
        }

        var http = context.HttpContext;
        var userAgent = http.Request.Headers.UserAgent.ToString();
        var sessionId = await sessions
            .StartAsync(
                userId,
                http.Connection.RemoteIpAddress?.ToString(),
                userAgent.Length == 0 ? null : userAgent,
                context.Properties.IsPersistent,
                http.RequestAborted)
            .ConfigureAwait(false);

        identity.AddClaim(new Claim(AuthPrincipal.SessionClaim, sessionId.ToString()));
    }

    public override async Task SigningOut(CookieSigningOutContext context)
    {
        if (AuthPrincipal.SessionId(context.HttpContext.User) is { } sessionId)
        {
            await sessions.EndAsync(sessionId, context.HttpContext.RequestAborted).ConfigureAwait(false);
        }
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        // A cookie with no session id predates sessions, or was forged without the key: either way it is not honoured.
        var principal = AuthPrincipal.SessionId(context.Principal) is { } sessionId
            ? await sessions.ResumeAsync(sessionId, context.HttpContext.RequestAborted).ConfigureAwait(false)
            : null;

        if (principal is null)
        {
            context.RejectPrincipal();
            await context.HttpContext
                .SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
                .ConfigureAwait(false);
            return;
        }

        context.ReplacePrincipal(principal);
    }
}

/// <summary>A bearer token names its session too, so ending the session ends the token.</summary>
internal static class AuthBearerEvents
{
    internal static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var sessions = context.HttpContext.RequestServices.GetRequiredService<IAuthSessions>();

        var principal = AuthPrincipal.SessionId(context.Principal) is { } sessionId
            ? await sessions.ResumeAsync(sessionId, context.HttpContext.RequestAborted).ConfigureAwait(false)
            : null;

        if (principal is null)
        {
            context.Fail("The session this token belongs to has ended.");
            return;
        }

        context.Principal = principal;
    }
}

/// <summary>Removes ended and long-expired session rows once an hour.</summary>
internal sealed class SessionSweep<TContext>(
    IDbContextFactory<TContext> contexts,
    TimeProvider clock,
    ILogger<SessionSweep<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);

        do
        {
            try
            {
                // A day's grace after expiry, so a device list can still say "signed out yesterday".
                var cutoff = clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(1);

                await using var db = await contexts.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
                await db.Set<Session>()
                    .IgnoreQueryFilters()
                    .Where(s => s.DeletedAt != null || s.ExpiresAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A database that is not migrated yet, or briefly unreachable: try again next hour.
                logger.LogDebug(exception, "The session sweep could not run this time.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
