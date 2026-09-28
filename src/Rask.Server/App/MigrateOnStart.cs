using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask;

/// <summary>
/// Applies the app's pending migrations when the host starts, before anything that needs the tables runs.
/// </summary>
/// <remarks>
/// <para>
/// In <see cref="StartingAsync"/>, which the host runs for every lifecycle service before it calls ANY hosted
/// service's <c>StartAsync</c> — so it is ahead of the jobs, outbox and mail workers and of the web server itself,
/// whatever order they were registered in. Until it is done nothing is listening, so <c>/health</c> cannot answer
/// Healthy over an un-migrated database. At start rather than at <c>Build</c>, so a test that builds the app and
/// never starts it touches no database.
/// </para>
/// <para>
/// Two instances starting together are EF Core's to settle: <c>Migrate</c> takes a database lock on PostgreSQL and
/// SQL Server, and a SQLite app is one process.
/// </para>
/// </remarks>
internal sealed class MigrateOnStart<TContext>(IDbContextFactory<TContext> contexts, ILoggerFactory loggers)
    : IHostedLifecycleService
    where TContext : DbContext
{
    private readonly ILogger _log = loggers.CreateLogger("Rask.Migrations");

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            if (!db.Database.IsRelational())
            {
                return;
            }

            var context = typeof(TContext).Name;

            // A warning, not a failure: an app whose schema comes from somewhere else (EnsureCreated in a prototype,
            // a test host) still starts. And never EnsureCreated here — a schema no migration made cannot be evolved
            // by the first one that comes along.
            if (!db.Database.GetMigrations().Any())
            {
                _log.LogWarning(
                    "{Context} has no migrations yet, so the database was left as it is. Create the first one with "
                    + "`rask db add Init`; the app applies it the next time it starts.",
                    context);
                return;
            }

            // What EF 9+ would throw from Migrate as PendingModelChangesWarning, said with the fix. Never suppressed:
            // a model change that silently never reaches the database is the bug this exists to prevent.
            if (db.Database.HasPendingModelChanges())
            {
                throw new InvalidOperationException(
                    $"The {context} model has changed since its last migration, so the database cannot be brought up "
                    + "to date. Add a migration for the change with `rask db add <Name>` and start the app again — it "
                    + "applies the new migration itself.");
            }

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (pending.Count == 0)
            {
                return;
            }

            var names = string.Join(", ", pending);
            _log.LogInformation("Applying {Count} migration(s) to the database: {Migrations}", pending.Count, names);

            try
            {
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Applying {names} to the database failed, so the app did not start: {e.Message} Fix the cause "
                    + "and start again. If a release step applies migrations instead, turn this off with "
                    + "app.Configure(c => c.MigrateOnStart = false) or Rask__Database__MigrateOnStart=false.",
                    e);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
