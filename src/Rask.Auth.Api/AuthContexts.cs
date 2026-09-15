using Microsoft.EntityFrameworkCore;

namespace Rask.Auth;

/// <summary>
/// Hands out a short-lived context of the application's own type.
/// </summary>
/// <remarks>
/// A seam rather than an <c>IDbContextFactory&lt;TContext&gt;</c> injected directly, because
/// <see cref="AccountService{TUser}" /> is generic over the <em>user</em> type and knows nothing about the application's
/// context type. This closes over it once, at registration.
/// </remarks>
internal interface IAuthContexts
{
    /// <summary>A context of its own, for one operation.</summary>
    Task<DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAuthContexts" />
/// <typeparam name="TContext">The application context that owns the account tables.</typeparam>
internal sealed class AuthContexts<TContext>(IDbContextFactory<TContext> factory) : IAuthContexts
    where TContext : DbContext
{
    public async Task<DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
}
