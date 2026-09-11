using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.MySql;

/// <summary>
/// Entity Framework Core entry point: Oracle's <c>UseMySQL</c> provider with production session settings applied on
/// every connection the context opens, a client command timeout, and transient-failure retrying.
/// </summary>
public static class RaskMySqlDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use MySQL with <paramref name="connectionString"/>, applies the
    /// <see cref="MySqlOptions"/> session settings on every connection open, retries transient failures, and stores
    /// every <see cref="DateTimeOffset"/> as its UTC instant so it keeps its fractional seconds. Swapping an existing
    /// <c>UseMySQL(cs)</c> for <c>UseRaskMySql(cs)</c> needs a migration, because those columns become
    /// <c>datetime(6)</c>.
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="connectionString">The MySQL connection string.</param>
    /// <param name="configure">Overrides for the production defaults.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The driver takes no session variables in the connection string, so <c>innodb_lock_wait_timeout</c> and
    /// <c>max_execution_time</c> are one <c>SET</c> sent each time EF opens a connection — one round trip per open.
    /// A connection opened directly on the <c>DbConnection</c> skips it; open through
    /// <c>context.Database.OpenConnectionAsync()</c>. Calling this twice keeps one interceptor and the last call's
    /// settings.
    /// </para>
    /// <para>
    /// Retrying is the provider's own execution strategy, which does not allow a transaction opened outside it: wrap
    /// a hand-written <c>BeginTransaction</c> in <c>context.Database.CreateExecutionStrategy().ExecuteAsync(...)</c>,
    /// or set <c>o.Retry.Enabled = false</c>. <c>SaveChanges</c> and Rask's own batteries need nothing.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseRaskMySql(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<MySqlOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var options = new MySqlOptions();
        configure?.Invoke(options);
        options.Validate();

        // Replaces any earlier call's extension, so the latest settings are the ones the interceptor reads.
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new RaskMySqlOptionsExtension(options));

        optionsBuilder.UseMySQL(connectionString, mySql =>
        {
            // MySQL's statement timeout covers SELECT only, so this client-side ceiling bounds every write.
            // Rounded up: the driver reads 0 as "wait forever".
            mySql.CommandTimeout((int)MySqlSessionSettings.Seconds(options.CommandTimeout));

            if (options.Retry.Enabled)
            {
                // The provider's own strategy: it already knows MySQL's transient error numbers.
                mySql.EnableRetryOnFailure(options.Retry.MaxCount, options.Retry.MaxDelay, errorNumbersToAdd: null);
            }
        });

        // One interceptor per configuration, however many times this is called.
        var interceptors = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors;
        if (interceptors is null || !interceptors.Any(static interceptor => interceptor is RaskMySqlConnectionInterceptor))
        {
            optionsBuilder.AddInterceptors(new RaskMySqlConnectionInterceptor());
        }

        return optionsBuilder;
    }

    /// <summary>
    /// The strongly-typed overload of
    /// <see cref="UseRaskMySql(DbContextOptionsBuilder, string, Action{MySqlOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskMySql(cs).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="connectionString">The MySQL connection string.</param>
    /// <param name="configure">Overrides for the production defaults.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseRaskMySql<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<MySqlOptions>? configure = null)
        where TContext : DbContext
    {
        UseRaskMySql((DbContextOptionsBuilder)optionsBuilder, connectionString, configure);
        return optionsBuilder;
    }
}
