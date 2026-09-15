using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Post.Search(text) through Rask.Data's context-less reads: the search is composed before any context exists, and
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
                INSERT INTO "Posts" ("Id", "Title", "Body") VALUES
                  (1, 'Charlie', 'search search search'),
                  (2, 'Alpha', 'a long body that mentions search once among many other words'),
                  (3, 'Bravo', 'nothing relevant');
                """);
        }

        Db.Configure(() => new JournalContext(options));
    }

    [Fact]
    public async Task Post_Search_reads_matches_best_first()
    {
        var titles = (await Post.Search("search").ToListAsync()).Select(p => p.Title);

        Assert.Equal(["Charlie", "Alpha"], titles);
    }

    [Fact]
    public async Task Post_Search_composes_with_the_model_query_operators()
    {
        Assert.Equal(["Alpha"], (await Post.Search("search").Where(p => p.Title != "Charlie").ToListAsync()).Select(p => p.Title));
        Assert.Equal(["Alpha", "Charlie"], (await Post.Search("search").OrderBy(p => p.Title).ToListAsync()).Select(p => p.Title));
        Assert.Equal(2, await Post.Search("search").CountAsync());
        Assert.Equal(3, await Post.Search("  ").CountAsync());
    }

    [Fact]
    public async Task Post_Search_projects_highlights()
    {
        var excerpts = await Post.Search("mentions").Select(p => FullText.Snippet(p.Body, 3)).ToListAsync();

        // Which window FTS5 picks is its own scoring; what Rask owns is the marking, the cut and the length.
        var excerpt = Assert.Single(excerpts)!;
        Assert.Contains($"{FullText.MatchStart}mentions{FullText.MatchEnd}", excerpt, StringComparison.Ordinal);
        Assert.StartsWith("…", excerpt, StringComparison.Ordinal);
        Assert.Equal(3, excerpt.Trim('…').Split(' ').Length);
    }

    [Fact]
    public void AsQueryable_carries_the_search_into_a_grids_count_order_and_page()
    {
        Expression<Func<Post, object?>> byTitle = p => p.Title;

        var query = Post.Search("search").AsQueryable();

        Assert.Equal(2, query.Count());
        Assert.Equal(["Charlie", "Alpha"], query.Select(p => p.Title).ToList());
        Assert.Equal(["Charlie"], query.OrderBy(byTitle).Skip(1).Take(1).ToList().Select(p => p.Title));
    }

    public void Dispose()
    {
        Db.Reset();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    internal sealed class Post : Model<int>
    {
        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;
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
