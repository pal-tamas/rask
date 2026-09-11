using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Hosting.Shared;

namespace Rask.SQLite;

/// <summary>Registers Rask.SQLite's raw-ADO.NET connection factory into an <see cref="IServiceCollection"/>.</summary>
public static class SqliteServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="ISqlite"/> that opens connections for the <c>Rask:ConnectionStrings:App</c>
    /// connection string with the production pragmas applied on every open. The pragmas read the
    /// <c>Rask:Sqlite</c> configuration section first and then <paramref name="configure"/>, so code wins.
    /// For Entity Framework Core use <c>UseRaskSqlite</c> on the <c>DbContextOptionsBuilder</c> instead — this is
    /// for code that uses SQLite directly. Idempotent: a second call is a no-op.
    /// </summary>
    public static IServiceCollection AddRaskSqlite(
        this IServiceCollection services,
        Action<SqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Idempotent: a second registration (e.g. a shared library and the app host both call it) is a
        // no-op, so the first call's options win consistently.
        if (!services.AddRaskOptions<SqliteOptions>(
                "Rask:Sqlite",
                static (section, o) => section.Bind(o),
                configure,
                static o =>
                {
                    o.Validate();
                    o.Retry.Validate();
                }))
        {
            return services;
        }

        services.TryAddSingleton(static sp => sp.GetRequiredService<SqliteOptions>().Retry);

        // The connection string is read when the factory is first resolved — which is also where a missing one
        // is reported, naming the key to set, rather than on the first query.
        services.TryAddSingleton<ISqlite>(static sp =>
        {
            var options = sp.GetRequiredService<SqliteOptions>();
            return new RaskSqliteConnectionFactory(
                RaskOptionsRegistration.ConnectionString(sp, "App"), options, options.Retry);
        });

        return services;
    }
}
