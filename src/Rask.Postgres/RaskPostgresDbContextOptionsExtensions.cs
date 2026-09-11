using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Rask.Hosting.Shared;

namespace Rask.Postgres;

/// <summary>
/// Entity Framework Core entry point: a drop-in replacement for <c>UseNpgsql</c> that also gives every session
/// the production timeouts, and turns on transient-failure retrying.
/// </summary>
public static class RaskPostgresDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use PostgreSQL with the <c>Rask:ConnectionStrings:App</c> connection string, gives
    /// every session the <see cref="PostgresOptions"/> timeouts — defaults, then the <c>Rask:Postgres</c>
    /// configuration section, then <paramref name="configure"/> — and retries transient failures.
    /// <code>
    /// builder.Services.AddDbContextFactory&lt;AppDbContext&gt;((sp, o) =&gt; o.UseRaskPostgres(sp));
    /// </code>
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="services">The application's services, which carry its configuration.</param>
    /// <param name="configure">Overrides for the production defaults, applied after the <c>Rask:Postgres</c> section.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// A missing <c>Rask:ConnectionStrings:App</c> is an error that names the key, never a guessed server.
    /// </para>
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
    /// or set <c>Retry.Enabled</c> to <c>false</c>. <c>SaveChanges</c> and Rask's own batteries need nothing.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseRaskPostgres(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider services,
        Action<PostgresOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(services);

        var connectionString = RaskOptionsRegistration.ConnectionString(services, "App");
        var options = RaskOptionsRegistration.BindNow<PostgresOptions>(
            services, "Rask:Postgres", static (section, o) => section.Bind(o), configure);

        try
        {
            options.Validate();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Reported like every other bad setting — which section, and why — since the value may well have
            // come from appsettings rather than the callback.
            throw new OptionsValidationException(
                Microsoft.Extensions.Options.Options.DefaultName, typeof(PostgresOptions), [$"Rask:Postgres: {ex.Message}"]);
        }

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
    /// <see cref="UseRaskPostgres(DbContextOptionsBuilder, IServiceProvider, Action{PostgresOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskPostgres(services).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="services">The application's services, which carry its configuration.</param>
    /// <param name="configure">Overrides for the production defaults, applied after the <c>Rask:Postgres</c> section.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseRaskPostgres<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        IServiceProvider services,
        Action<PostgresOptions>? configure = null)
        where TContext : DbContext
    {
        UseRaskPostgres((DbContextOptionsBuilder)optionsBuilder, services, configure);
        return optionsBuilder;
    }
}
