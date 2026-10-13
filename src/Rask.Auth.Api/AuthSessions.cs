using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Auth;

/// <inheritdoc cref="IAuthSessions" />
/// <remarks>
/// Every resume reads the session's row and its user's: nothing is remembered between two of them. A live page resumes
/// before each event it takes, so a session ended, a user removed or a role changed on any replica is what the very next
/// event on every other one sees — which a per-process cache, however short, would stand in the way of.
/// </remarks>
internal sealed class AuthSessions<TContext, TUser>(
    IDbContextFactory<TContext> contexts,
    TimeProvider clock,
    AuthOptions options) : IAuthSessions
    where TContext : DbContext
    where TUser : Authenticatable
{
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

            // TenantId rides along so a restored session filters for the same tenant the sign-in did, and the
            // account's own claims so it is the principal the sign-in issued.
            return AuthPrincipal.For(
                found.UserId, found.User.Email, [.. found.User.Roles], sessionId, found.User.TenantId, found.User.OwnClaims());
        }
    }

    public async Task EndAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
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
                session.End(now);
                db.Remove(session);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return sessions.Count;
        }
    }
}
