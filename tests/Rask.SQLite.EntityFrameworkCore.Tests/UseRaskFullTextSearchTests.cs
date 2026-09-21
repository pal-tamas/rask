using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// #1110: full-text search for a context opened with a plain UseSqlite — the way a browser app opens its database —
// without UseRaskSqlite's connection string, pragmas and retry.
public sealed class UseRaskFullTextSearchTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-fts-opt-in-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task A_plain_UseSqlite_context_migrates_the_index_and_searches_it()
    {
        await using var db = new ArticleContext(Plain().UseRaskFullTextSearch().Options);
        TestMigrations.Apply(db);

        db.Articles.AddRange(
            new Article { Id = 1, Title = "All about SQLite", Body = "Small and fast." },
            new Article { Id = 2, Title = "Kittens", Body = "Nothing to do with storage." });
        await db.SaveChangesAsync();

        Assert.Equal(["All about SQLite"], await db.Articles.Search("sqlite").Select(a => a.Title).ToListAsync());
    }

    [Fact]
    public void It_installs_a_generator_the_boot_check_accepts()
    {
        // ProviderFeatureCheck refuses to boot a context that declares an index its generator cannot build; Rask's
        // own generator is the one that can.
        using var db = new ArticleContext(Plain().UseRaskFullTextSearch().Options);

        Assert.IsType<RaskSqliteRangeExclusionSqlGenerator>(db.GetService<IMigrationsSqlGenerator>());
    }

    [Fact]
    public void After_UseRaskSqlite_it_keeps_that_calls_generator_strict_tables_included()
    {
        // EF keeps one generator and the last registration wins; replacing it here would silently drop STRICT.
        var options = new DbContextOptionsBuilder<ArticleContext>()
            .UseRaskSqliteAt($"Data Source={_dbPath}", o => o.StrictTables = true)
            .UseRaskFullTextSearch()
            .Options;
        using var db = new ArticleContext(options);

        Assert.IsType<RaskSqliteStrictRangeExclusionSqlGenerator>(db.GetService<IMigrationsSqlGenerator>());
    }

    [Fact]
    public async Task Calling_it_twice_changes_nothing()
    {
        await using var db = new ArticleContext(Plain().UseRaskFullTextSearch().UseRaskFullTextSearch().Options);
        TestMigrations.Apply(db);

        db.Articles.Add(new Article { Id = 1, Title = "All about SQLite", Body = "Small." });
        await db.SaveChangesAsync();

        Assert.Single(await db.Articles.Search("sqlite").ToListAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private DbContextOptionsBuilder<ArticleContext> Plain() =>
        new DbContextOptionsBuilder<ArticleContext>().UseSqlite($"Data Source={_dbPath}");
}
