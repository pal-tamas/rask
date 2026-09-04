using Microsoft.EntityFrameworkCore;

namespace Rask.Auth;

/// <summary>Reads and writes the single row that records who claimed this instance.</summary>
internal interface IInstanceClaimStore
{
    /// <summary>Whether an account has already claimed this instance.</summary>
    Task<bool> IsClaimedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the instance for <paramref name="userId"/>, returning whether this caller won.
    /// </summary>
    /// <remarks>
    /// <c>false</c> means somebody else claimed it — either earlier, or concurrently. The caller
    /// becomes an ordinary user; it must not retry or treat this as an error.
    /// </remarks>
    Task<bool> TryClaimAsync(string userId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IInstanceClaimStore"/>
/// <typeparam name="TContext">The application context that owns the auth tables.</typeparam>
internal sealed class InstanceClaimStore<TContext>(
    IDbContextFactory<TContext> factory, TimeProvider clock) : IInstanceClaimStore
    where TContext : DbContext
{
    public async Task<bool> IsClaimedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<AuthInstanceClaim>()
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryClaimAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<AuthInstanceClaim>().Add(new AuthInstanceClaim
        {
            Id = AuthInstanceClaim.SingletonId,
            AdminUserId = userId,
            ClaimedUtc = clock.GetUtcNow().UtcDateTime,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            // The primary key is a constant, so this is the losing half of a race: somebody else
            // claimed the instance between our read and our write. That is the designed outcome, not a
            // fault — the caller simply becomes an ordinary user. Catching the constraint violation is
            // the point of the table; it is what removes the need to trust an isolation level.
            return false;
        }
    }
}
