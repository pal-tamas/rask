using Microsoft.EntityFrameworkCore;

namespace Rask.Auth;

/// <inheritdoc cref="IAuthContexts" />
/// <typeparam name="TContext">The application context that owns the account tables.</typeparam>
internal sealed class AuthContexts<TContext>(IDbContextFactory<TContext> factory) : IAuthContexts
    where TContext : DbContext
{
    public async Task<DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
}
