using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Rask.Example.Wasm.Jobs;

/// <summary>
///     Creates the schema on first run, once the database file has been restored.
/// </summary>
/// <remarks>
///     <para>
///         A plain <see cref="IHostedService" /> registered between <c>AddRaskBrowserSqlite</c> and
///         <c>AddRaskJobs</c>: registration order is start order, and a plain hosted service does its work
///         inside <c>StartAsync</c>, so by the time the job processor starts, the tables exist.
///     </para>
///     <para>
///         <c>EnsureCreatedAsync</c> is right for a demo and wrong for an app that will ever ship a second
///         version: it creates the schema only when the database does not exist, so a restored snapshot
///         predating a new table never gains it. A real app wants migrations.
///     </para>
/// </remarks>
public sealed class SchemaInitializer(IDbContextFactory<AppDbContext> factory, DatabaseReady ready) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        // Releases any component already waiting in OnMountAsync — see DatabaseReady for why one is.
        ready.Signal();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
