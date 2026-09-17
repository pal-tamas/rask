using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Post.Read.Search(text) through Rask.Data's context-less reads: the search is composed before any context exists, and
// replayed onto a fresh one at execution — including through AsQueryable(), the shape UiDataGrid counts, orders and
// pages. Db is process-wide, which this assembly already accepts by running its tests one at a time.
public sealed class FullTextModelSearchTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-fts-model-{Guid.NewGuid():N}.db");

    public FullTextModelSearchTests()
    {
        var options = new DbContextOptionsBuilder<JournalContext>().UseRaskSqliteAt($"Data Source={_dbPath}").Options;
        using (var db = new JournalContext(options))
        {
            TestMigrations.Apply(db);
            db.Database.ExecuteSqlRaw(
                """
                INSERT INTO "Posts" ("Id", "Title", "Body", "CreatedAt", "UpdatedAt", "Version") VALUES
                  (1, 'Charlie', 'search search search', '2026-01-01 00:00:00', '2026-01-01 00:00:00', 0),
                  (2, 'Alpha', 'a long body that mentions search once among many other words', '2026-01-01 00:00:00', '2026-01-01 00:00:00', 0),
                  (3, 'Bravo', 'nothing relevant', '2026-01-01 00:00:00', '2026-01-01 00:00:00', 0);
                """);
        }

        Db.Configure(() => new JournalContext(options));

        // The read faces query through a context of their own, mirrored from the write model above — so the
        // full-text index JournalContext declares is what Post.Read.Search reaches.
        var read = new DbContextOptionsBuilder<RaskReadDbContext>()
            .UseRaskSqliteAt($"Data Source={_dbPath}").Options;
        ReadDb.Configure(() => new RaskReadDbContext(read));
    }

    [Fact]
    public async Task Post_Search_reads_matches_best_first()
    {
        var titles = (await Post.Read.Search("search").ToListAsync()).Select(p => p.Title);

        Assert.Equal(["Charlie", "Alpha"], titles);
    }

    [Fact]
    public async Task Post_Search_composes_with_the_model_query_operators()
    {
        Assert.Equal(["Alpha"], (await Post.Read.Search("search").Where(p => p.Title != "Charlie").ToListAsync()).Select(p => p.Title));
        Assert.Equal(["Alpha", "Charlie"], (await Post.Read.Search("search").OrderBy(p => p.Title).ToListAsync()).Select(p => p.Title));
        Assert.Equal(2, await Post.Read.Search("search").CountAsync());
        Assert.Equal(3, await Post.Read.Search("  ").CountAsync());
    }

    [Fact]
    public async Task ThenBy_composes_onto_best_match_order()
    {
        // Search counts as an ordering, so a tie-breaker is allowed after it and has to translate after the rewrite.
        var titles = (await Post.Read.Search("search").ThenByDescending(p => p.Title).ToListAsync()).Select(p => p.Title);

        Assert.Equal(["Charlie", "Alpha"], titles);
    }

    [Fact]
    public async Task ThenBy_after_an_empty_search_orders_instead_of_failing()
    {
        // The box is empty on first render: the page must not crash exactly when nothing has been typed yet.
        var titles = (await Post.Read.Search("").ThenBy(p => p.Title).ToListAsync()).Select(p => p.Title);

        Assert.Equal(["Alpha", "Bravo", "Charlie"], titles);
    }

    [Fact]
    public async Task Post_Search_projects_highlights()
    {
        var excerpts = await Post.Read.Search("mentions").Select(p => FullText.Snippet(p.Body, 3)).ToListAsync();

        // Which window FTS5 picks is its own scoring; what Rask owns is the marking, the cut and the length.
        var excerpt = Assert.Single(excerpts)!;
        Assert.Contains($"{FullText.MatchStart}mentions{FullText.MatchEnd}", excerpt, StringComparison.Ordinal);
        Assert.StartsWith("…", excerpt, StringComparison.Ordinal);
        Assert.Equal(3, excerpt.Trim('…').Split(' ').Length);
    }

    [Fact]
    public void AsQueryable_carries_the_search_into_a_grids_count_order_and_page()
    {
        Expression<Func<PostRead, object?>> byTitle = p => p.Title;

        var query = Post.Read.Search("search").AsQueryable();

        Assert.Equal(2, query.Count());
        Assert.Equal(["Charlie", "Alpha"], query.Select(p => p.Title).ToList());
        Assert.Equal(["Charlie"], query.OrderBy(byTitle).Skip(1).Take(1).ToList().Select(p => p.Title));
    }

    public void Dispose()
    {
        Db.Reset();
        ReadDb.Reset();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }


    private sealed class JournalContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Post>(post =>
            {
                post.ToTable("Posts");
                post.HasFullTextSearch(p => new { p.Title, p.Body });
            });
    }
}

internal sealed class Post : Aggregate<int>
{
    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;
}
