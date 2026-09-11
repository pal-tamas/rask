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
    /// file is <b>not</b> covered by <c>rask db backup</c> or Litestream.
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

        services.AddRaskOptions<RaskLoggingOptions>(
            "Rask:Logging", static (section, o) => section.Bind(o), configure, static o => o.Validate());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<LogMetrics>();
        services.TryAddSingleton<LogChannel>();

        // The connection string is read when the store is first resolved — which is also where a missing one is
        // reported, naming the key to set.
        services.TryAddSingleton<ILogs>(static sp => new SqliteLogStore(
            RaskOptionsRegistration.ConnectionString(sp, "Logs"),
            sp.GetRequiredService<RaskLoggingOptions>(),
            sp.GetRequiredService<TimeProvider>()));

        // Registered as a logging provider rather than a bespoke channel, so the store sees exactly what
        // every other sink sees. TryAddEnumerable keys on the implementation type, so a repeated
        // AddRaskLogging call doesn't double-capture every entry.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, RaskLoggerProvider>());
        services.AddHostedService<LogWriter>();

        return services;
    }
}
