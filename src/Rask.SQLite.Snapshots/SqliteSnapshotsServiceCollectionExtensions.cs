using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.SQLite.Snapshots;

/// <summary>Registers scheduled SQLite snapshots into an <see cref="IServiceCollection"/>.</summary>
public static class SqliteSnapshotsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the snapshotter and a background service that snapshots the database on
    /// <see cref="SqliteSnapshotOptions.Interval"/>. Uses a <see cref="DirectorySnapshotStore"/> over
    /// <see cref="SqliteSnapshotOptions.DestinationDirectory"/> unless you have already registered your
    /// own <see cref="ISqliteSnapshotStore"/>. Idempotent.
    /// </summary>
    public static IServiceCollection AddRaskSqliteSnapshots(
        this IServiceCollection services,
        Action<SqliteSnapshotOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Idempotent: a second call is a no-op so the snapshot service isn't scheduled twice.
        if (services.Any(static d => d.ServiceType == typeof(SnapshotMarker)))
        {
            return services;
        }

        services.AddSingleton(new SnapshotMarker());

        var options = new SqliteSnapshotOptions();
        configure(options);

        // A custom store (e.g. object storage) supplies its own destination, so DestinationDirectory is
        // only required for the built-in directory store.
        var hasCustomStore = services.Any(static d => d.ServiceType == typeof(ISqliteSnapshotStore));
        options.Validate(requireDestinationDirectory: !hasCustomStore);

        services.TryAddSingleton(options);

        if (!hasCustomStore)
        {
            // Scope pruning to this database's own snapshots so a shared directory stays safe.
            var stem = Path.GetFileNameWithoutExtension(options.DatabasePath!);
            var searchPattern = $"{stem}-*.db";
            services.TryAddSingleton<ISqliteSnapshotStore>(
                new DirectorySnapshotStore(options.DestinationDirectory!, searchPattern));
        }

        services.TryAddSingleton<ISqliteSnapshotter, SqliteSnapshotter>();
        services.AddHostedService<SqliteSnapshotService>();

        return services;
    }

    // Sentinel marking that AddRaskSqliteSnapshots already ran on this collection.
    private sealed class SnapshotMarker;
}
