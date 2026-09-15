using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Rask.Data;
using Rask.SQLite;

namespace Rask.Benchmarks;

// What HasFullTextSearch buys over the search box every app writes first. Both arms ask for the first page of
// posts mentioning one word, through EF Core against the same WAL database:
//
//   Contains  — Where(p => p.Title.Contains(w) || p.Body.Contains(w)).Take(20): LIKE '%w%' over both columns of
//               every row until 20 match. Unranked, and it matches w inside longer words.
//   Search    — Search(w).Take(20): the FTS5 index scanned for w, each match joined to its row by key, best
//               match first. Ranking means reading every match, not stopping at 20.
//
// The word is rare on purpose (about 1 row in 1,000): a common word lets LIKE stop early and hides the scan,
// and a search box is most useful — and a scan most expensive — exactly when the word is rare.
[MemoryDiagnoser]
public class SqliteFullTextSearchBenchmarks
{
    private const string RareWord = "zeppelin";

    private string _dbPath = null!;
    private DbContextOptions<SearchBenchContext> _options = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"rask-fts-bench-{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<SearchBenchContext>()
            .UseRaskSqliteAt($"Data Source={_dbPath}")
            .Options;

        using var context = new SearchBenchContext(_options);

        // The index exists only through migrations, so the schema goes through the differ and the generator.
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
        {
            context.Database.ExecuteSqlRaw(command.CommandText);
        }

        string[] words = ["sqlite", "index", "query", "page", "row", "table", "search", "fast", "small", "file", "write", "read"];
        var random = new Random(42);

        context.Database.OpenConnection();
        using var transaction = context.Database.BeginTransaction();
        using var insert = context.Database.GetDbConnection().CreateCommand();
        insert.CommandText = """INSERT INTO "Posts" ("Title", "Body") VALUES ($title, $body);""";
        var title = insert.CreateParameter();
        title.ParameterName = "$title";
        var body = insert.CreateParameter();
        body.ParameterName = "$body";
        insert.Parameters.Add(title);
        insert.Parameters.Add(body);

        for (var i = 0; i < Rows; i++)
        {
            var text = string.Join(' ', Enumerable.Range(0, 40).Select(_ => words[random.Next(words.Length)]));
            title.Value = $"Post {i}";
            body.Value = i % 1_000 == 7 ? $"{text} {RareWord}" : text;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<int> Contains()
    {
        await using var context = new SearchBenchContext(_options);
        var page = await context.Set<SearchBenchPost>()
            .Where(p => p.Title.Contains(RareWord) || p.Body.Contains(RareWord))
            .Take(20)
            .ToListAsync();
        return page.Count;
    }

    [Benchmark]
    public async Task<int> Search()
    {
        await using var context = new SearchBenchContext(_options);
        var page = await context.Set<SearchBenchPost>().Search(RareWord).Take(20).ToListAsync();
        return page.Count;
    }

    public sealed class SearchBenchPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;
    }

    public sealed class SearchBenchContext(DbContextOptions<SearchBenchContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<SearchBenchPost>(post =>
            {
                post.ToTable("Posts");
                post.HasFullTextSearch(p => new { p.Title, p.Body });
            });
    }
}
