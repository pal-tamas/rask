using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
/// Every resume reads the session's row and its user's: no answer is remembered between two of them. A live page asks
/// before each event it takes, so a session ended, a user removed or a role changed on any replica is what the very next
/// event on every other one sees — which a per-process cache, however short, would stand in the way of.
/// </remarks>
internal sealed class AuthSessions<TContext, TUser>(
    IDbContextFactory<TContext> contexts,
    TimeProvider clock,
    AuthOptions options) : IAuthSessions, IAsyncDisposable
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

    // Compiled once: a live page resumes before every event, and translating the same LINQ each time was most of
    // what a resume cost. Both query filters apply: an ended (soft-deleted) session and a deleted user are not found.
    private static readonly Func<TContext, Guid, CancellationToken, Task<Row?>> FindRow =
        EF.CompileAsyncQuery((TContext db, Guid sessionId, CancellationToken _) =>
            (from s in db.Set<Session>().AsNoTracking()
             join u in db.Set<TUser>().AsNoTracking() on s.UserId equals u.Id
             where s.Id == sessionId
             select new Row(u, s.ExpiresAt, s.LastSeenAt)).FirstOrDefault());

    // What each principal handed out was built from, for as long as somebody holds it: a live page asks about the
    // one it holds, and is handed that same one back while its user's row still says what it said. Not a cache —
    // the row is read every time, and this only saves building an identical principal from it.
    private readonly ConditionalWeakTable<ClaimsPrincipal, Issued> _issued = [];

    public async Task<ClaimsPrincipal?> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await ReadAsync(sessionId, cancellationToken).ConfigureAwait(false) is { } row ? Issue(row, sessionId) : null;

    public async ValueTask<ClaimsPrincipal?> RevalidateAsync(
        Guid sessionId, ClaimsPrincipal held, CancellationToken cancellationToken = default)
    {
        if (await ReadAsync(sessionId, cancellationToken).ConfigureAwait(false) is not { } row)
        {
            return null;
        }

        return _issued.TryGetValue(held, out var issued) && issued.StillSays(row.User) ? held : Issue(row, sessionId);
    }

    // The session's row and its user's, or null when the session has ended, expired, or its user is gone.
    private async Task<Row?> ReadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var db = _readers.TryTake(out var kept) ? kept : await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var sound = false;
        try
        {
            var found = await FindRow(db, sessionId, cancellationToken).ConfigureAwait(false);
            if (found is not null && found.ExpiresAt > now && now - found.LastSeenAt >= TimeSpan.FromMinutes(1))
            {
                await TouchAsync(db, sessionId, now, found.ExpiresAt, cancellationToken).ConfigureAwait(false);
            }

            sound = true;
            return found is null || found.ExpiresAt <= now ? null : found;
        }
        finally
        {
            await KeepOrDisposeAsync(db, sound).ConfigureAwait(false);
        }
    }

    // At most once a minute: a write per request would make every page view a database write.
    private Task<int> TouchAsync(TContext db, Guid sessionId, DateTime now, DateTime expires, CancellationToken cancellationToken)
    {
        var expiresAt = options.SlidingExpiration ? now + options.ExpireTimeSpan : expires;
        return db.Set<Session>()
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(s => s.LastSeenAt, now).SetProperty(s => s.ExpiresAt, expiresAt),
                cancellationToken);
    }

    // The contexts these reads run on are kept and used again: building one allocated three times what the read
    // itself does, and a read tracks nothing, so one that finished cleanly holds nothing of the call before. One
    // whose read threw is not trusted with another.
    private readonly ConcurrentBag<TContext> _readers = [];
    private const int ReadersKept = 8;
    private volatile bool _disposed;

    private ValueTask KeepOrDisposeAsync(TContext db, bool sound)
    {
        if (!sound || _disposed || _readers.Count >= ReadersKept)
        {
            return db.DisposeAsync();
        }

        _readers.Add(db);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        while (_readers.TryTake(out var db))
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    // TenantId rides along so a restored session filters for the same tenant the sign-in did, and the account's
    // own claims so it is the principal the sign-in issued.
    private ClaimsPrincipal Issue(Row row, Guid sessionId)
    {
        var issued = Issued.From(row.User);
        var principal = AuthPrincipal.For(
            row.User.Id, issued.Email, issued.Roles, sessionId, issued.TenantId, issued.OwnClaims);
        _issued.Add(principal, issued);

        return principal;
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

    private sealed record Row(TUser User, DateTime ExpiresAt, DateTime LastSeenAt);

    // Everything of a user's row that reaches their principal.
    private sealed record Issued(string Email, string[] Roles, Guid? TenantId, OwnClaim[] OwnClaims)
    {
        internal static Issued From(TUser user) => new(user.Email, [.. user.Roles], user.TenantId, user.OwnClaims());

        internal bool StillSays(TUser user) =>
            string.Equals(Email, user.Email, StringComparison.Ordinal)
            && TenantId == user.TenantId
            && Roles.AsSpan().SequenceEqual([.. user.Roles])
            && OwnClaims.AsSpan().SequenceEqual(user.OwnClaims());
    }
}
