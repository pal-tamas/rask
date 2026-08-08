using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Browser;
using Rask.SQLite.Snapshots;

namespace Rask.SQLite.Browser;

/// <summary>Registers a persistent, browser-hosted SQLite database.</summary>
public static class BrowserSqliteServiceCollectionExtensions
{
    /// <summary>
    ///     Makes the SQLite database <paramref name="name" /> durable in the browser: restored from
    ///     IndexedDB before anything opens it, written back on an interval and on page-hide, and owned by
    ///     exactly one tab.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Point your <c>DbContext</c> at the matching connection string, and everything above it —
    ///         including <c>AddRaskJobs&lt;TContext&gt;()</c> — reads exactly as it does on the server:
    ///     </para>
    ///     <code>
    ///     builder.Services.AddRaskBrowserSqlite("app");
    ///     builder.Services.AddDbContextFactory&lt;AppDbContext&gt;(o =&gt;
    ///         o.UseSqlite(BrowserSqlite.ConnectionString("app")));
    ///     builder.Services.AddRaskJobs&lt;AppDbContext&gt;();
    ///     </code>
    ///     <para>
    ///         Call this <b>before</b> anything that opens the database, because registration order is
    ///         start order: the restore runs inside its hosted service's <c>StartAsync</c> precisely so
    ///         that later services find a populated file.
    ///     </para>
    ///     <para>
    ///         An app using Entity Framework Core on top of this must publish with
    ///         <c>PublishTrimmed=false</c> — EF Core does not survive the trimmer in a browser build.
    ///         Microsoft.Data.Sqlite on its own does.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddRaskBrowserSqlite(
        this IServiceCollection services,
        string name = "app",
        Action<BrowserSqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new BrowserSqliteOptions { Name = name };
        configure?.Invoke(options);
        options.Validate();

        // Idempotent per database name: registering the same one twice would elect two owners in one tab
        // and snapshot it twice on every tick.
        if (services.Any(d => d.ServiceType == typeof(BrowserSqliteOptions)
                && d.ImplementationInstance is BrowserSqliteOptions existing
                && string.Equals(existing.Name, options.Name, StringComparison.Ordinal)))
        {
            return services;
        }

        services.AddSingleton(options);

        // The snapshotter needs its own options type; DatabasePath is the one field that matters here,
        // since the destination is our IndexedDB store rather than a directory.
        services.TryAddSingleton(new SqliteSnapshotOptions
        {
            DatabasePath = options.DatabasePath,
            Interval = options.SnapshotInterval,
            Retain = options.Retain,
        });

        services.TryAddSingleton<ISqliteSnapshotStore>(sp =>
            new IndexedDbSnapshotStore(
                sp.GetRequiredService<IIndexedDb>(),
                BrowserSqlite.SnapshotStoreName(options.Name)));

        services.TryAddSingleton<ISqliteSnapshotter, SqliteSnapshotter>();

        // Registration order is start order, and it matters twice: the host must restore before anything
        // opens the database, and the snapshot loop must not tick before the restore finished.
        services.AddSingleton<BrowserSqliteHost>();
        services.AddHostedService(sp => sp.GetRequiredService<BrowserSqliteHost>());
        services.AddHostedService<BrowserSqliteSnapshotService>();

        return services;
    }
}
