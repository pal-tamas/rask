using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Hosting;
using Rask.Data;

namespace Rask.SQLite.Browser.Fixture.Wasm;

public sealed class Article
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
}

public sealed class ArticleContext(DbContextOptions<ArticleContext> options) : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Article>().HasFullTextSearch(a => new { a.Title, a.Body });
}

/// <summary>Completes once the table, its index and the rows exist.</summary>
public sealed class SchemaReady
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Task => _ready.Task;

    internal void Set() => _ready.TrySetResult();

    internal void Fail(Exception error) => _ready.TrySetException(error);
}

/// <summary>
///     Builds the schema the way a migration does — the model differ, then the migrations SQL generator — because that is
///     the only path that creates the full-text index: <c>EnsureCreated</c> builds the table without it. Then seeds it.
/// </summary>
public sealed class ArticleSchema(IDbContextFactory<ArticleContext> contexts, SchemaReady ready) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);

            // A snapshot restored from IndexedDB already has it all.
            var exists = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name = 'Articles'")
                .SingleAsync(cancellationToken);

            if (exists == 0)
            {
                var model = db.GetService<IDesignTimeModel>().Model;
                var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
                foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
                {
                    await db.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken);
                }

                db.Articles.AddRange(
                    new Article { Title = "All about SQLite", Body = "A small, fast database that runs inside the browser tab." },
                    new Article { Title = "Offline first", Body = "Keep working without a network: storage lives on the device." },
                    new Article { Title = "Kittens", Body = "Nothing to do with databases at all." });
                await db.SaveChangesAsync(cancellationToken);
            }

            ready.Set();
        }
        catch (Exception error)
        {
            // Shown on the page, so a failure says what broke instead of timing out on an empty list.
            ready.Fail(error);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
