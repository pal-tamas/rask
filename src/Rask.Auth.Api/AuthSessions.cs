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
internal sealed class AuthSessions<TContext, TUser, TKey>(
    IDbContextFactory<TContext> contexts,
    TimeProvider clock,
    AuthOptions options) : IAuthSessions, IAsyncDisposable
    where TContext : DbContext
    where TUser : Authenticatable<TKey>
    where TKey : notnull
{
    // The key named by its name, so the one query below serves a Guid key without a cast inside it.
    private const string KeyProperty = nameof(Authenticatable.Id);

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
    // A Guid key joins the session to its user in one query; a text key reads them one after the other, because the
    // session holds the Guid the text carries and turning one into the other inside SQL is spelled differently on
    // every database.
    private static readonly Func<TContext, Guid, CancellationToken, Task<Row?>> FindRow =
        typeof(TKey) == typeof(Guid) ? Joined() : FindApart;

    private static Func<TContext, Guid, CancellationToken, Task<Row?>> Joined() =>
        EF.CompileAsyncQuery((TContext db, Guid sessionId, CancellationToken _) =>
            (from s in db.Set<Session>().AsNoTracking()
             join u in db.Set<TUser>().AsNoTracking() on s.UserId equals EF.Property<Guid>(u, KeyProperty)
             where s.Id == sessionId
             select new Row(u, s.ExpiresAt, s.LastSeenAt, s.ActingInTenant)).FirstOrDefault());

    private static readonly Func<TContext, Guid, CancellationToken, Task<SessionRow?>> FindSession =
        EF.CompileAsyncQuery((TContext db, Guid sessionId, CancellationToken _) =>
            db.Set<Session>().AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new SessionRow(s.UserId, s.ExpiresAt, s.LastSeenAt, s.ActingInTenant))
                .FirstOrDefault());

    private static readonly Func<TContext, string, CancellationToken, Task<TUser?>> FindUserByText =
        EF.CompileAsyncQuery((TContext db, string key, CancellationToken _) =>
            db.Set<TUser>().AsNoTracking().FirstOrDefault(u => EF.Property<string>(u, KeyProperty) == key));

    private static async Task<Row?> FindApart(TContext db, Guid sessionId, CancellationToken cancellationToken)
    {
        if (await FindSession(db, sessionId, cancellationToken).ConfigureAwait(false) is not { } session)
        {
            return null;
        }

        return await FindUserByText(db, session.UserId.ToString(), cancellationToken).ConfigureAwait(false) is { } user
            ? new Row(user, session.ExpiresAt, session.LastSeenAt, session.ActingInTenant)
            : null;
    }

    // What each principal handed out was built from, for as long as somebody holds it: a live page asks about the
    // one it holds, and is handed that same one back while its user's row still says what it said. Not a cache —
    // the row is read every time, and this only saves building an identical principal from it.
    private readonly ConditionalWeakTable<ClaimsPrincipal, Issued> _issued = [];

    public async Task<ClaimsPrincipal?> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await ReadAsync(sessionId, used: true, cancellationToken).ConfigureAwait(false) is { } row ? Issue(row, sessionId) : null;

    public async ValueTask<ClaimsPrincipal?> RevalidateAsync(
        Guid sessionId, ClaimsPrincipal held, bool used, CancellationToken cancellationToken = default)
    {
        if (await ReadAsync(sessionId, used, cancellationToken).ConfigureAwait(false) is not { } row)
        {
            return null;
        }

        return _issued.TryGetValue(held, out var issued) && issued.StillSays(row, ActingIn(row)) ? held : Issue(row, sessionId);
    }

    // The session's row and its user's, or null when the session has ended, expired, or its user is gone.
    // used: a request or a reader's action is behind the read, so the session is seen and its expiry slides.
    private async Task<Row?> ReadAsync(Guid sessionId, bool used, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var db = _readers.TryTake(out var kept) ? kept : await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var sound = false;
        try
        {
            var found = await FindRow(db, sessionId, cancellationToken).ConfigureAwait(false);
            if (used && found is not null && found.ExpiresAt > now && now - found.LastSeenAt >= TimeSpan.FromMinutes(1))
            {
                await TouchAsync(db, sessionId, now, found.ExpiresAt, cancellationToken).ConfigureAwait(false);
            }

            sound = true;
            if (found is null || found.ExpiresAt <= now)
            {
                return null;
            }

            // A name is read through its column's mapping, so while the context is at hand; an address is the row's own.
            return AccountName<TUser, TKey>.IsEmail ? found : found with { Name = AccountName<TUser, TKey>.Of(db.Model, found.User) };
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
    // own claims so it is the principal the sign-in issued. A user of no tenant who may choose one carries the
    // tenant this session chose instead.
    private ClaimsPrincipal Issue(Row row, Guid sessionId)
    {
        var issued = Issued.From(row, ActingIn(row));
        var principal = AuthPrincipal.For(
            row.User.AccountId, issued.Name, issued.Email, issued.Roles, sessionId, issued.TenantId ?? issued.ActingIn, issued.OwnClaims);
        _issued.Add(principal, issued);

        return principal;
    }

    // The tenant the session chose, while its user still may: belongs to none, and holds the choosing role.
    private Guid? ActingIn(Row row) =>
        row.ActingInTenant is { } chosen && TenantChoice.MayChoose(row.User, options) ? chosen : null;

    public async Task<bool> ActInTenantAsync(Guid sessionId, Guid? tenant, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // The soft-delete filter applies, so an ended session is not given a tenant back.
            var session = await db.Set<Session>()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.ExpiresAt > now, cancellationToken)
                .ConfigureAwait(false);

            if (session is null)
            {
                return false;
            }

            session.ActIn(tenant);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
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

    private sealed record Row(TUser User, DateTime ExpiresAt, DateTime LastSeenAt, Guid? ActingInTenant)
    {
        // What the user signs in with, for an account that signs in by name; its address otherwise, which needs no copy.
        public string? Name { get; init; }

        public string SignInName => Name ?? User.Email;
    }

    private sealed record SessionRow(Guid UserId, DateTime ExpiresAt, DateTime LastSeenAt, Guid? ActingInTenant);

    // Everything of a user's row that reaches their principal.
    private sealed record Issued(
        string Name, string Email, string[] Roles, Guid? TenantId, Guid? ActingIn, OwnClaim[] OwnClaims)
    {
        internal static Issued From(Row row, Guid? actingIn) =>
            new(row.SignInName, row.User.Email, [.. row.User.Roles], row.User.TenantId, actingIn, row.User.OwnClaims());

        internal bool StillSays(Row row, Guid? actingIn) =>
            string.Equals(Name, row.SignInName, StringComparison.Ordinal)
            && string.Equals(Email, row.User.Email, StringComparison.Ordinal)
            && TenantId == row.User.TenantId
            && ActingIn == actingIn
            && Roles.AsSpan().SequenceEqual([.. row.User.Roles])
            && OwnClaims.AsSpan().SequenceEqual(row.User.OwnClaims());
    }
}
