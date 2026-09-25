using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Hosting;
using Rask.Data;

namespace Rask.Site.DataDemo;

/// <summary>Completes once the model surface is configured and the table, its index and the seed rows exist.</summary>
/// <remarks>
///     A browser app starts its hosted services after the first render, so the page asks this before its first read
///     rather than racing the schema. A failure is kept, so the page can say what broke instead of spinning.
/// </remarks>
public sealed class NotesReady
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the database is ready, or faults with why it is not.</summary>
    public Task Task => _ready.Task;

    internal void Set() => _ready.TrySetResult();

    internal void Fail(Exception error) => _ready.TrySetException(error);
}

/// <summary>
///     Builds the schema and seeds it on the first visit. Registered after <c>AddRaskBrowserSqlite</c>, so a
///     returning visitor's database has been restored from IndexedDB before this looks at it.
/// </summary>
public sealed class NotesDatabase(IDbContextFactory<NotesDb> contexts, NotesReady ready) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);

            // A database restored from IndexedDB already has it all.
            var exists = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'Note'")
                .SingleAsync(cancellationToken);

            if (exists == 0)
            {
                // The schema the way a migration builds it — the model differ, then the migrations SQL generator —
                // because that is the path that creates the full-text index; EnsureCreated builds the table only.
                var model = db.GetService<IDesignTimeModel>().Model;
                var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
                foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
                {
                    await db.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken);
                }

                foreach (var (title, body) in Seed)
                {
                    await Note.Create(Note.Write(title, body), cancellationToken: cancellationToken);
                }
            }

            ready.Set();
        }
        catch (Exception error)
        {
            ready.Fail(error);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static readonly (string Title, string Body)[] Seed =
    [
        ("Welcome to Rask", "Every note here lives in a SQLite database inside this browser tab, written through EF Core."),
        ("Offline first", "Close the tab and come back: the database is kept in IndexedDB and restored before the page reads it."),
        ("Full-text search", "Type a word above. Matches are ranked best first, and the matched words are highlighted."),
    ];
}
