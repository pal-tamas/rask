using Microsoft.EntityFrameworkCore;

namespace Rask.SqlServer;

/// <summary>
/// Entity Framework Core entry point: a drop-in replacement for <c>UseSqlServer</c> that also wires the
/// production session settings onto every connection the context opens, and turns on transient-failure
/// retrying.
/// </summary>
public static class RaskSqlServerDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use SQL Server with <paramref name="connectionString"/>, applies the
    /// <see cref="RaskSqlServerOptions"/> session settings (production defaults, overridable via
    /// <paramref name="configure"/>) on every connection open, and enables retry-on-failure. Swap your
    /// <c>UseSqlServer(cs)</c> for <c>UseRaskSqlServer(cs)</c> and you are done.
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="configure">Overrides for the production defaults.</param>
    public static DbContextOptionsBuilder UseRaskSqlServer(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<RaskSqlServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var options = new RaskSqlServerOptions();
        configure?.Invoke(options);
        options.Validate();

        return optionsBuilder
            .UseSqlServer(connectionString, sqlServer =>
            {
                // SQL Server has no server-side statement timeout, so unlike PostgreSQL this ceiling has to
                // be set on the client. Rounded up: a sub-second timeout would truncate to zero, which means
                // "wait forever" — the exact opposite of what was asked for.
                sqlServer.CommandTimeout((int)Math.Ceiling(options.CommandTimeout.TotalSeconds));

                if (options.MaxRetryCount > 0)
                {
                    // SqlServerRetryingExecutionStrategy: it already knows which error numbers are transient,
                    // including the Azure SQL failover set, and that list is the part worth not reimplementing.
                    sqlServer.EnableRetryOnFailure(options.MaxRetryCount, options.MaxRetryDelay, errorNumbersToAdd: null);
                }
            })
            .AddInterceptors(new RaskSqlServerConnectionInterceptor(options));
    }

    /// <summary>
    /// The strongly-typed overload of
    /// <see cref="UseRaskSqlServer(DbContextOptionsBuilder, string, Action{RaskSqlServerOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskSqlServer(cs).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseRaskSqlServer<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<RaskSqlServerOptions>? configure = null)
        where TContext : DbContext
    {
        UseRaskSqlServer((DbContextOptionsBuilder)optionsBuilder, connectionString, configure);
        return optionsBuilder;
    }
}
