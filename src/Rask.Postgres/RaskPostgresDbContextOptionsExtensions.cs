using Microsoft.EntityFrameworkCore;

namespace Rask.Postgres;

/// <summary>
/// Entity Framework Core entry point: a drop-in replacement for <c>UseNpgsql</c> that also wires the
/// production session settings onto every connection the context opens, and turns on transient-failure
/// retrying.
/// </summary>
public static class RaskPostgresDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use PostgreSQL with <paramref name="connectionString"/>, applies the
    /// <see cref="RaskPostgresOptions"/> session settings (production defaults, overridable via
    /// <paramref name="configure"/>) on every connection open, and enables retry-on-failure. Swap your
    /// <c>UseNpgsql(cs)</c> for <c>UseRaskPostgres(cs)</c> and you are done.
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="configure">Overrides for the production defaults.</param>
    public static DbContextOptionsBuilder UseRaskPostgres(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<RaskPostgresOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var options = new RaskPostgresOptions();
        configure?.Invoke(options);
        options.Validate();

        return optionsBuilder
            .UseNpgsql(connectionString, postgres =>
            {
                if (options.MaxRetryCount > 0)
                {
                    // Npgsql's own strategy, not a Rask one: it already knows which PostgreSQL error codes
                    // are transient, and that list is exactly the part worth not reimplementing.
                    postgres.EnableRetryOnFailure(options.MaxRetryCount, options.MaxRetryDelay, errorCodesToAdd: null);
                }
            })
            .AddInterceptors(new RaskPostgresConnectionInterceptor(options));
    }

    /// <summary>
    /// The strongly-typed overload of
    /// <see cref="UseRaskPostgres(DbContextOptionsBuilder, string, Action{RaskPostgresOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskPostgres(cs).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseRaskPostgres<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<RaskPostgresOptions>? configure = null)
        where TContext : DbContext
    {
        UseRaskPostgres((DbContextOptionsBuilder)optionsBuilder, connectionString, configure);
        return optionsBuilder;
    }
}
