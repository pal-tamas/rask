using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Postgres;
using Rask.SQLite;
using Rask.SqlServer;

namespace Rask;

/// <summary>
/// Entity Framework Core entry point for a Rask app whose database is chosen by configuration: SQLite, PostgreSQL or
/// SQL Server, named by <c>Rask:Database:Provider</c>.
/// </summary>
public static class RaskDatabaseDbContextOptionsExtensions
{
    /// <summary>
    /// Opens the application database <c>Rask:Database:Provider</c> names — <c>sqlite</c> (the default),
    /// <c>postgres</c> or <c>sqlserver</c> — at <c>Rask:ConnectionStrings:App</c>, through <c>UseRaskSqlite</c>,
    /// <c>UseRaskPostgres</c> or <c>UseRaskSqlServer</c>.
    /// <code>
    /// builder.Services.AddDbContextFactory&lt;AppDbContext&gt;((sp, o) =&gt; o
    ///     .UseRaskDatabase(sp)
    ///     .AddInterceptors(sp.GetServices&lt;ISaveChangesInterceptor&gt;()));
    /// </code>
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="services">The application's service provider, whose configuration names the provider.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Each provider still tunes itself from its own section — <c>Rask:Sqlite</c>, <c>Rask:Postgres</c> or
    /// <c>Rask:SqlServer</c> — so this takes no options; call the provider's own method instead when a callback has to
    /// set something configuration cannot.
    /// </para>
    /// <para>
    /// The key is matched without regard to case, and a value that names no database throws naming the choices. Moving an
    /// app is more than the setting: migrations are provider-specific, so they are generated against the new provider;
    /// and in a <c>RaskApp</c> on PostgreSQL or SQL Server the log lives in this database, so a context of the app's own
    /// maps it with <c>modelBuilder.AddRaskLogging()</c>.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseRaskDatabase(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(services);

        return RaskDatabase.Provider(services.GetService<IConfiguration>()) switch
        {
            RaskDatabaseProvider.Postgres => optionsBuilder.UseRaskPostgres(services),
            RaskDatabaseProvider.SqlServer => optionsBuilder.UseRaskSqlServer(services),
            _ => optionsBuilder.UseRaskSqlite(services),
        };
    }

    /// <summary>
    /// The strongly-typed overload of <see cref="UseRaskDatabase(DbContextOptionsBuilder, IServiceProvider)"/>, so the
    /// builder keeps its <see cref="DbContextOptionsBuilder{TContext}"/> type.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="services">The application's service provider, whose configuration names the provider.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseRaskDatabase<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        IServiceProvider services)
        where TContext : DbContext
    {
        UseRaskDatabase((DbContextOptionsBuilder)optionsBuilder, services);
        return optionsBuilder;
    }
}
