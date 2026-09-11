using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Hosting.Shared;

namespace Rask.SQLite.Snapshots;

/// <summary>Registers scheduled SQLite snapshots into an <see cref="IServiceCollection"/>.</summary>
public static class SqliteSnapshotsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the snapshotter and a background service that snapshots the database on
    /// <see cref="SqliteSnapshotOptions.Interval"/>. The options read the <c>Rask:Snapshots</c> configuration section
    /// first and then <paramref name="configure"/>, so code wins; <see cref="SqliteSnapshotOptions.DatabasePath"/>
    /// defaults to the file named by <c>Rask:ConnectionStrings:App</c>. Uses a <see cref="DirectorySnapshotStore"/>
    /// over <see cref="SqliteSnapshotOptions.DestinationDirectory"/> unless you have already registered your own
    /// <see cref="ISqliteSnapshotStore"/>. Idempotent.
    /// </summary>
    public static IServiceCollection AddRaskSqliteSnapshots(
        this IServiceCollection services,
        Action<SqliteSnapshotOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // A custom store (e.g. object storage) supplies its own destination, so DestinationDirectory is
        // only required for the built-in directory store. Decided from what was registered before this call.
        var hasCustomStore = services.Any(static d => d.ServiceType == typeof(ISqliteSnapshotStore));

        // Idempotent: a second call is a no-op so the snapshot service isn't scheduled twice.
        if (!services.AddRaskOptions<SqliteSnapshotOptions>(
                "Rask:Snapshots",
                static (section, o) => section.Bind(o),
                configure,
                o => o.Validate(requireDestinationDirectory: !hasCustomStore)))
        {
            return services;
        }

        // The database being snapshotted is the app's own unless something said otherwise. PostConfigure, so a path
        // from the section or the callback wins and validation still sees the result.
        services.AddOptions<SqliteSnapshotOptions>().PostConfigure<IServiceProvider>(static (o, sp) =>
            o.DatabasePath = string.IsNullOrEmpty(o.DatabasePath) ? AppDatabasePath(sp) ?? o.DatabasePath : o.DatabasePath);

        if (!hasCustomStore)
        {
            services.TryAddSingleton<ISqliteSnapshotStore>(static sp =>
            {
                var options = sp.GetRequiredService<SqliteSnapshotOptions>();

                // Scope pruning to this database's own snapshots so a shared directory stays safe.
                var stem = Path.GetFileNameWithoutExtension(options.DatabasePath!);
                return new DirectorySnapshotStore(options.DestinationDirectory!, $"{stem}-*.db");
            });
        }

        services.TryAddSingleton<ISqliteSnapshotter, SqliteSnapshotter>();
        services.AddHostedService<SqliteSnapshotService>();

        return services;
    }

    // The file behind Rask:ConnectionStrings:App, or null when there is none. Rask.SQLite.Litestream carries the same
    // two lines: sharing them through InternalsVisibleTo would put a second copy of the source-linked options helper
    // in sight of this assembly.
    private static string? AppDatabasePath(IServiceProvider services)
    {
        if (services.GetService<IConfiguration>()?["Rask:ConnectionStrings:App"] is not { Length: > 0 } connectionString)
        {
            return null;
        }

        try
        {
            return new SqliteConnectionStringBuilder(connectionString).DataSource;
        }
        catch (ArgumentException)
        {
            // Not a SQLite connection string — an app on another database. There is no file to derive, so validation
            // reports DatabasePath under Rask:Snapshots instead of the parser's "Keyword not supported" naming neither.
            return null;
        }
    }
}
