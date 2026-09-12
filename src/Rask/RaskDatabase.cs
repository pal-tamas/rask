using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Postgres;
using Rask.SQLite;
using Rask.SqlServer;

namespace Rask;

/// <summary>The databases <c>Rask:Database:Provider</c> can name.</summary>
internal enum RaskDatabaseProvider
{
    Sqlite,
    Postgres,
    SqlServer,
}

/// <summary>Reads <c>Rask:Database:Provider</c>, the one setting that decides which database an app opens.</summary>
internal static class RaskDatabase
{
    internal const string ProviderKey = "Rask:Database:Provider";

    /// <summary>
    /// The provider <paramref name="configuration"/> names — SQLite when it names none, so an app that never set the
    /// key keeps the database it always had.
    /// </summary>
    internal static RaskDatabaseProvider Provider(IConfiguration? configuration)
    {
        var value = configuration?[ProviderKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return RaskDatabaseProvider.Sqlite;
        }

        foreach (var provider in (ReadOnlySpan<RaskDatabaseProvider>)
                 [RaskDatabaseProvider.Sqlite, RaskDatabaseProvider.Postgres, RaskDatabaseProvider.SqlServer])
        {
            if (string.Equals(value.Trim(), Name(provider), StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        throw new InvalidOperationException(
            $"{ProviderKey} is '{value}', which is not a database Rask can open. Set it to sqlite, postgres or sqlserver "
            + "(Rask__Database__Provider in the environment), or remove it to keep SQLite.");
    }

    /// <summary>The value <c>Rask:Database:Provider</c> spells <paramref name="provider"/> with.</summary>
    internal static string Name(RaskDatabaseProvider provider) => provider switch
    {
        RaskDatabaseProvider.Sqlite => "sqlite",
        RaskDatabaseProvider.Postgres => "postgres",
        RaskDatabaseProvider.SqlServer => "sqlserver",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>What EF Core reports as <c>Database.ProviderName</c> for <paramref name="provider"/>.</summary>
    internal static string EfProviderName(RaskDatabaseProvider provider) => provider switch
    {
        RaskDatabaseProvider.Sqlite => "Microsoft.EntityFrameworkCore.Sqlite",
        RaskDatabaseProvider.Postgres => "Npgsql.EntityFrameworkCore.PostgreSQL",
        RaskDatabaseProvider.SqlServer => "Microsoft.EntityFrameworkCore.SqlServer",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };
}

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

/// <summary>
/// Fails the start when the app's own context opens a different database than <c>Rask:Database:Provider</c> names.
/// </summary>
/// <remarks>
/// <para>
/// <c>RaskApp</c> wires the batteries by that setting — where the log goes, whether snapshots run — while the app's own
/// <c>AddDbContextFactory</c> call decides where its data goes. A Program.cs still calling <c>UseRaskSqlite(sp)</c> after
/// the setting moved to <c>postgres</c> would split the two silently, so the disagreement is reported at start, naming
/// the call that keeps them together. Registered only for an app-owned context: <c>RaskAppDbContext</c> is wired with
/// <c>UseRaskDatabase</c> and cannot disagree.
/// </para>
/// <para>
/// An options type, validated on start, rather than a hosted service: options are validated before any hosted service
/// starts, in registration order, and this one is registered ahead of every battery — so a battery that fails on the
/// wrong database cannot report first and hide the mistake that caused it.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The application context <c>RaskApp</c> wired the batteries against.</typeparam>
internal sealed class RaskDatabaseProviderCheck<TContext>
    where TContext : DbContext
{
    /// <summary>Throws when the context's provider is not the configured one; returns <c>true</c> otherwise.</summary>
    internal bool Verify(IDbContextFactory<TContext> contexts, IConfiguration configuration)
    {
        var expected = RaskDatabase.Provider(configuration);

        using var db = contexts.CreateDbContext();
        var actual = db.Database.ProviderName;
        if (string.Equals(actual, RaskDatabase.EfProviderName(expected), StringComparison.Ordinal))
        {
            return true;
        }

        throw new InvalidOperationException(
            $"{RaskDatabase.ProviderKey} is {RaskDatabase.Name(expected)}, but {typeof(TContext).Name} opens "
            + $"{actual ?? "no database provider"}, so the batteries would be wired for one database while the app talks to "
            + $"another. Register the context with AddDbContextFactory<{typeof(TContext).Name}>((sp, o) => "
            + "o.UseRaskDatabase(sp)) so the setting picks its provider.");
    }
}
