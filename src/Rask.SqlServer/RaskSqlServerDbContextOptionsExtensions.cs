using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Rask.Hosting.Shared;

namespace Rask.SqlServer;

/// <summary>
/// Entity Framework Core entry point: a drop-in replacement for <c>UseSqlServer</c> that also applies the
/// production session settings on every connection the context opens, sets a client command timeout, and turns
/// on transient-failure retrying.
/// </summary>
public static class RaskSqlServerDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use SQL Server with the <c>Rask:ConnectionStrings:App</c> connection string, applies
    /// the <see cref="SqlServerOptions"/> session settings on every connection open — defaults, then the
    /// <c>Rask:SqlServer</c> configuration section, then <paramref name="configure"/> — and retries transient failures.
    /// <code>
    /// builder.Services.AddDbContextFactory&lt;AppDbContext&gt;((sp, o) =&gt; o.UseRaskSqlServer(sp));
    /// </code>
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="services">The application's services, which carry its configuration.</param>
    /// <param name="configure">Overrides for the production defaults, applied after the <c>Rask:SqlServer</c> section.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// A missing <c>Rask:ConnectionStrings:App</c> is an error that names the key, never a guessed server.
    /// </para>
    /// <para>
    /// SQL Server takes no session settings in the connection string, so <c>XACT_ABORT</c> and <c>LOCK_TIMEOUT</c>
    /// are sent as one batch each time EF opens a connection — one round trip per open. A connection opened
    /// directly on the <c>DbConnection</c> skips it; open through <c>context.Database.OpenConnectionAsync()</c>.
    /// </para>
    /// <para>
    /// Retrying is SQL Server's own execution strategy, which does not allow a transaction opened outside it: wrap
    /// a hand-written <c>BeginTransaction</c> in <c>context.Database.CreateExecutionStrategy().ExecuteAsync(...)</c>,
    /// or set <c>Retry.Enabled</c> to <c>false</c>. <c>SaveChanges</c> and Rask's own batteries need nothing.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseRaskSqlServer(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider services,
        Action<SqlServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(services);

        var connectionString = RaskOptionsRegistration.ConnectionString(services, "App");
        var options = RaskOptionsRegistration.BindNow<SqlServerOptions>(
            services, "Rask:SqlServer", static (section, o) => section.Bind(o), configure);

        try
        {
            options.Validate();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Reported like every other bad setting — which section, and why — since the value may well have
            // come from appsettings rather than the callback.
            throw new OptionsValidationException(
                Microsoft.Extensions.Options.Options.DefaultName, typeof(SqlServerOptions), [$"Rask:SqlServer: {ex.Message}"]);
        }

        // Replaces any earlier call's extension, so the latest settings are the ones the interceptor reads.
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new RaskSqlServerOptionsExtension(options));

        optionsBuilder.UseSqlServer(connectionString, sqlServer =>
        {
            // SQL Server has no server-side statement timeout, so this client-side ceiling is the only one.
            // Rounded up: SqlClient reads 0 as "wait forever".
            sqlServer.CommandTimeout((int)SqlServerSessionSettings.Seconds(options.CommandTimeout));

            if (options.Retry.Enabled)
            {
                // SQL Server's own strategy: it already knows the transient error numbers, the Azure SQL
                // failover set included, and that list is the part worth not reimplementing.
                sqlServer.EnableRetryOnFailure(options.Retry.MaxCount, options.Retry.MaxDelay, errorNumbersToAdd: null);
            }
        });

        // One interceptor per configuration, however many times this is called: interceptors accumulate across
        // calls, and it reads its settings off the extension the latest call replaced.
        var interceptors = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors;
        if (interceptors is null || !interceptors.Any(static interceptor => interceptor is RaskSqlServerConnectionInterceptor))
        {
            optionsBuilder.AddInterceptors(new RaskSqlServerConnectionInterceptor());
        }

        return optionsBuilder;
    }

    /// <summary>
    /// The strongly-typed overload of
    /// <see cref="UseRaskSqlServer(DbContextOptionsBuilder, IServiceProvider, Action{SqlServerOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskSqlServer(services).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="services">The application's services, which carry its configuration.</param>
    /// <param name="configure">Overrides for the production defaults, applied after the <c>Rask:SqlServer</c> section.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseRaskSqlServer<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        IServiceProvider services,
        Action<SqlServerOptions>? configure = null)
        where TContext : DbContext
    {
        UseRaskSqlServer((DbContextOptionsBuilder)optionsBuilder, services, configure);
        return optionsBuilder;
    }
}
