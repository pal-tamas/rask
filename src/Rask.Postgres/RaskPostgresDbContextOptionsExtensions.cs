using Microsoft.EntityFrameworkCore;

namespace Rask.Postgres;

/// <summary>
/// Entity Framework Core entry point: a drop-in replacement for <c>UseNpgsql</c> that also gives every session
/// the production timeouts, and turns on transient-failure retrying.
/// </summary>
public static class RaskPostgresDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use PostgreSQL with <paramref name="connectionString"/>, gives every session
    /// the <see cref="PostgresOptions"/> timeouts, and retries transient failures. Swap your
    /// <c>UseNpgsql(cs)</c> for <c>UseRaskPostgres(cs)</c> and you are done.
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="configure">Overrides for the production defaults.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The timeouts travel as startup parameters in the connection string (<c>Options=-c statement_timeout=…</c>),
    /// so they are the session's defaults: they survive the pool's reset, cost no round trip per open, and reach
    /// code that opens the <c>DbConnection</c> directly. Behind PgBouncer in transaction mode, add <c>options</c>
    /// to its <c>ignore_startup_parameters</c>, or set the timeouts to <see cref="TimeSpan.Zero"/> and configure
    /// them on the database role. A <c>-c</c> the connection string already carries wins over Rask's.
    /// </para>
    /// <para>
    /// Retrying is Npgsql's own execution strategy, which does not allow a transaction opened outside it: wrap a
    /// hand-written <c>BeginTransaction</c> in <c>context.Database.CreateExecutionStrategy().ExecuteAsync(...)</c>,
    /// or set <c>o.Retry.Enabled = false</c>. <c>SaveChanges</c> and Rask's own batteries need nothing.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseRaskPostgres(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<PostgresOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var options = new PostgresOptions();
        configure?.Invoke(options);
        options.Validate();

        return optionsBuilder.UseNpgsql(PostgresSessionSettings.Apply(connectionString, options), postgres =>
        {
            if (options.Retry.Enabled)
            {
                // Npgsql's own strategy, not a Rask one: it already knows which PostgreSQL error codes are
                // transient, and that list is exactly the part worth not reimplementing.
                postgres.EnableRetryOnFailure(options.Retry.MaxCount, options.Retry.MaxDelay, errorCodesToAdd: null);
            }
        });
    }

    /// <summary>
    /// The strongly-typed overload of
    /// <see cref="UseRaskPostgres(DbContextOptionsBuilder, string, Action{PostgresOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskPostgres(cs).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="configure">Overrides for the production defaults.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseRaskPostgres<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<PostgresOptions>? configure = null)
        where TContext : DbContext
    {
        UseRaskPostgres((DbContextOptionsBuilder)optionsBuilder, connectionString, configure);
        return optionsBuilder;
    }
}
