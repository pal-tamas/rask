using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Hosting;
using Rask.Data;

namespace Rask.Site.DataDemo;

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
            var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var dbScope = db.ConfigureAwait(false);

            // A database restored from IndexedDB already has it all.
            var exists = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'Note'")
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            if (exists == 0)
            {
                // The schema the way a migration builds it — the model differ, then the migrations SQL generator —
                // because that is the path that creates the full-text index; EnsureCreated builds the table only.
                var model = db.GetService<IDesignTimeModel>().Model;
                var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
                foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
                {
                    await db.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken).ConfigureAwait(false);
                }

                foreach (var (title, body) in Seed)
                {
                    await Note.Create(Note.Write(title, body), cancellationToken: cancellationToken).ConfigureAwait(false);
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
