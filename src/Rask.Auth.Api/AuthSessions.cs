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
    private static TimeSpan CacheFor => TimeSpan.FromSeconds(30);

    public async Task<Guid> StartAsync(
        Guid userId, string? ipAddress, string? userAgent, bool persistent, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var session = Session.Start(userId, ipAddress, userAgent, persistent, now, options.ExpireTimeSpan);

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            db.Add(session);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return session.Id;
        }
    }

    public async Task<ClaimsPrincipal?> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        if (cache.TryGetValue(CacheKey(sessionId), out Snapshot? cached) && cached is not null)
        {
            if (cached.ExpiresAt > now)
            {
                return AuthPrincipal.For(cached.UserId, cached.Email, cached.Roles, sessionId, cached.TenantId, cached.OwnClaims);
            }

            cache.Remove(CacheKey(sessionId));
        }

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Both query filters apply: an ended (soft-deleted) session and a deleted user are not found.
            var found = await (
                    from s in db.Set<Session>().AsNoTracking()
                    join u in db.Set<TUser>().AsNoTracking() on s.UserId equals u.Id
                    where s.Id == sessionId
                    select new { s.UserId, User = u, s.ExpiresAt, s.LastSeenAt })
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

            var snapshot = new Snapshot(
                found.UserId, found.User.Email, [.. found.User.Roles], expiresAt, found.User.TenantId, found.User.OwnClaims());
            cache.Set(CacheKey(sessionId), snapshot, CacheFor);

            return AuthPrincipal.For(
                snapshot.UserId, snapshot.Email, snapshot.Roles, sessionId, snapshot.TenantId, snapshot.OwnClaims);
        }
    }

    public async Task EndAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cache.Remove(CacheKey(sessionId));

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            if (await db.Set<Session>().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false)
                is not { } session)
            {
                return;
            }

            session.End(clock.GetUtcNow().UtcDateTime);
            db.Remove(session);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> EndAllAsync(Guid userId, Guid? except = null, CancellationToken cancellationToken = default)
    {
        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
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
    }

    private static string CacheKey(Guid sessionId) => "Rask.Auth.Session:" + sessionId.ToString("N");

    // TenantId rides along so a restored session filters for the same tenant the sign-in did. Without it a
    // reconnect would come back with no tenant claim and every tenant-scoped read would throw.
    // The account's own claims ride along for the same reason: a session loaded again must be the principal the
    // sign-in issued, or a claim would be there until the first reconnect and gone after it.
    private sealed record Snapshot(
        Guid UserId, string Email, string[] Roles, DateTime ExpiresAt, Guid? TenantId, OwnClaim[] OwnClaims);
}
