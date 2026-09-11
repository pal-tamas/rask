using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Rask.Hosting.Shared;

namespace Rask.Logging;

/// <summary>Registers the durable log store into an <see cref="IServiceCollection"/>.</summary>
public static class RaskLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Captures the application's log into a SQLite database of its own, at the <c>Rask:ConnectionStrings:Logs</c>
    /// connection string, so what happened survives the restart that hid it.
    /// <code>
    /// builder.Services.AddRaskLogging();
    /// </code>
    /// <para>
    /// A connection string of its own rather than a <c>TContext</c> like the other database-backed pillars: the
    /// store deliberately owns its own file. See <see cref="ILogs"/> for why, and remember that the
    /// file is <b>not</b> covered by <c>rask db backup</c> or Litestream. On PostgreSQL or SQL Server, use
    /// <see cref="AddRaskLogging{TContext}"/> instead.
    /// </para>
    /// <para>
    /// <see cref="RaskLoggingOptions"/> reads the <c>Rask:Logging</c> configuration section first and then
    /// <paramref name="configure"/>, so code wins. The schema is created on first use — there is no migration to
    /// add. Entries below <see cref="RaskLoggingOptions.MinimumLevel"/> are skipped, and so is anything your
    /// <c>Logging:LogLevel</c> configuration already filtered, since that runs first.
    /// </para>
    /// </summary>
    public static IServiceCollection AddRaskLogging(
        this IServiceCollection services,
        Action<RaskLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The connection string is read when the store is first resolved — which is also where a missing one is
        // reported, naming the key to set.
        return AddStore(services, configure, static sp => new SqliteLogStore(
            RaskOptionsRegistration.ConnectionString(sp, "Logs"),
            sp.GetRequiredService<RaskLoggingOptions>(),
            sp.GetRequiredService<TimeProvider>()));
    }

    /// <summary>
    /// Captures the application's log into the application's own database, as the <c>RaskLog</c> table that
    /// <c>modelBuilder.AddRaskLogging()</c> maps onto <typeparamref name="TContext"/>. The store for an app on
    /// PostgreSQL or SQL Server.
    /// <code>
    /// builder.Services.AddRaskLogging&lt;AppDbContext&gt;();
    ///
    /// // AppDbContext.OnModelCreating
    /// modelBuilder.AddRaskLogging();   // then: rask db add AddLogs &amp;&amp; rask db update
    /// </code>
    /// <para>
    /// Every flush runs on a context and a connection of its own from <see cref="IDbContextFactory{TContext}"/>,
    /// so a line logged while one of your transactions is failing is committed on its own and survives the
    /// rollback, and the log is covered by the database's own backups. What EF Core logs on the store's behalf is
    /// not captured back into it; your own SQL still is. No connection string of its own: the store connects
    /// through <typeparamref name="TContext"/>.
    /// </para>
    /// <para>
    /// <see cref="RaskLoggingOptions"/> reads the <c>Rask:Logging</c> section first and then
    /// <paramref name="configure"/>, as the file store's do. On SQLite prefer
    /// <see cref="AddRaskLogging(IServiceCollection, Action{RaskLoggingOptions}?)"/>: a file of its own keeps a
    /// machine-rate writer off the single write lock your requests share. <see cref="RaskLoggingOptions.Pragmas"/>
    /// and <see cref="RaskLoggingOptions.BusyRetry"/> do not apply here. Register <typeparamref name="TContext"/> as
    /// an <see cref="IDbContextFactory{TContext}"/>; a model that never mapped the table fails the boot with the line
    /// to add. Idempotent.
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> whose model maps the log table.</typeparam>
    public static IServiceCollection AddRaskLogging<TContext>(
        this IServiceCollection services,
        Action<RaskLoggingOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Before the writer, so an unmapped model fails the boot with the line to type rather than as a flush
        // error. It reads the MODEL, never the database — see BatteryModelCheck.
        services.AddHostedService<LogsModelCheck<TContext>>();

        return AddStore(services, configure, static sp => new DbContextLogStore<TContext>(
            sp.GetRequiredService<IDbContextFactory<TContext>>(),
            sp.GetRequiredService<TimeProvider>()));
    }

    private static IServiceCollection AddStore(
        IServiceCollection services,
        Action<RaskLoggingOptions>? configure,
        Func<IServiceProvider, ILogs> store)
    {
        services.AddRaskOptions<RaskLoggingOptions>(
            "Rask:Logging", static (section, o) => section.Bind(o), configure, static o => o.Validate());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<LogMetrics>();
        services.TryAddSingleton<LogChannel>();

        // The first registration wins, as the first call's options do: an app that calls both overloads keeps the
        // store it registered first.
        services.TryAddSingleton(store);

        // Registered as a logging provider rather than a bespoke channel, so the store sees exactly what
        // every other sink sees. TryAddEnumerable keys on the implementation type, so a repeated
        // AddRaskLogging call doesn't double-capture every entry.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, RaskLoggerProvider>());
        services.AddHostedService<LogWriter>();

        return services;
    }
}
