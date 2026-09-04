using Microsoft.EntityFrameworkCore;

namespace Rask.Auth;

/// <summary>
/// Hands out a short-lived context for seeding the built-in roles.
/// </summary>
/// <remarks>
/// A seam rather than an <c>IDbContextFactory&lt;TContext&gt;</c> injected directly, because
/// <see cref="AccountService{TUser}" /> is generic over the <em>user</em> type and knows nothing about
/// the application's context type. This closes over it once, at registration.
/// </remarks>
internal interface IRoleSeedContexts
{
    /// <summary>A context of its own, for one seeding pass.</summary>
    Task<DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRoleSeedContexts" />
/// <typeparam name="TContext">The application context that owns the account tables.</typeparam>
internal sealed class RoleSeedContexts<TContext>(IDbContextFactory<TContext> factory) : IRoleSeedContexts
    where TContext : DbContext
{
    public async Task<DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
}
