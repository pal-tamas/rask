using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Rask.Data;

namespace Rask.Providers.E2E.Tests;

public sealed class SearchArticle
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public bool Published { get; set; }
}

public class SearchDbContext(DbContextOptions options) : DbContext(options)
{
    public const string Schema = "rask_e2e_fts";

    public DbSet<SearchArticle> Articles => Set<SearchArticle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SearchArticle>().HasFullTextSearch(a => new { a.Title, a.Body });
    }
}

public sealed class EnglishSearchDbContext(DbContextOptions<EnglishSearchDbContext> options) : SearchDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SearchArticle>().HasFullTextSearch(a => new { a.Title, a.Body }, FullTextTokenizer.English);
    }
}

public sealed class PlainSearchDbContext(DbContextOptions<PlainSearchDbContext> options) : DbContext(options)
{
    public DbSet<SearchArticle> Articles => Set<SearchArticle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SearchDbContext.Schema);
        modelBuilder.Entity<SearchArticle>();
    }
}

/// <summary>
///     HasFullTextSearch / Search(text) / FullText.Highlight and Snippet on PostgreSQL (#1109) — the same API as on
///     SQLite, backed by a generated tsvector column and a GIN index.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresFullTextSearchTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (Postgres.Available)
        {
            await using var db = Unicode();
            await Postgres.ResetSchemaAsync(db, SearchDbContext.Schema);
            db.Articles.AddRange(
                new SearchArticle { Id = 1, Title = "All about SQLite", Body = "SQLite is small, SQLite is fast, SQLite is everywhere." },
                new SearchArticle { Id = 2, Title = "Databases", Body = "Postgres and SQLite store rows in tables.", Published = true },
                new SearchArticle { Id = 3, Title = "Kittens", Body = "Nothing to do with storage." },
                new SearchArticle { Id = 4, Title = "Első kérés", Body = "Magyar szöveg." });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (Postgres.Available)
        {
            await using var db = Unicode();
            await Postgres.DropSchemaAsync(db, SearchDbContext.Schema);
        }
    }

    [SkippableFact]
    public async Task A_search_finds_its_words_best_match_first()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using var db = Unicode();

        // Article 1 says "sqlite" four times, article 2 once: rank, not id order.
        Assert.Equal([1, 2], await db.Articles.Search("sqlite").Select(a => a.Id).ToListAsync());
    }

    [SkippableFact]
    public async Task The_last_word_is_a_prefix_and_every_word_is_required()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using var db = Unicode();

        Assert.Equal([1, 2], await db.Articles.Search("sqli").Select(a => a.Id).ToListAsync());
        Assert.Equal([2], await db.Articles.Search("postgres sqli").Select(a => a.Id).ToListAsync());
    }

    [SkippableFact]
    public async Task The_unicode_tokenizer_ignores_accents_the_way_the_sqlite_one_does()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using var db = Unicode();

        Assert.Equal([4], await db.Articles.Search("keres").Select(a => a.Id).ToListAsync());
    }

    [SkippableFact]
    public async Task The_english_tokenizer_stems()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using (var reset = English())
        {
            await Postgres.ResetSchemaAsync(reset, SearchDbContext.Schema);
            reset.Articles.Add(new SearchArticle { Id = 9, Title = "Running", Body = "She runs every morning." });
            await reset.SaveChangesAsync();
        }

        await using var db = English();
        Assert.Equal([9], await db.Articles.Search("run").Select(a => a.Id).ToListAsync());
    }

    [SkippableFact]
    public async Task Punctuation_and_quotes_in_the_search_are_words_not_syntax()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using var db = Unicode();

        // A tsquery operator or a stray quote typed into a search box must not become a syntax error.
        Assert.Empty(await db.Articles.Search("it's & | ! (fast").ToListAsync());
        Assert.Equal([1], await db.Articles.Search("\"fast\"").Select(a => a.Id).ToListAsync());
    }

    [SkippableFact]
    public async Task A_filter_and_a_tie_breaker_compose_with_the_rank()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using var db = Unicode();

        Assert.Equal([2], await db.Articles.Search("sqlite").Where(a => a.Published).Select(a => a.Id).ToListAsync());
        // A read face's Search is ordered and takes ThenBy directly; a DbSet's is ordered all the same.
        var ranked = (IOrderedQueryable<SearchArticle>)db.Articles.Search("sqlite");
        Assert.Equal([1, 2], await ranked.ThenBy(a => a.Title).Select(a => a.Id).ToListAsync());
    }

    [SkippableFact]
    public async Task Highlight_and_snippet_mark_the_matches_for_UiHighlight()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using var db = Unicode();

        var hit = await db.Articles.Search("keres")
            .Select(a => new { Title = FullText.Highlight(a.Title), Body = FullText.Snippet(a.Body, 3) })
            .SingleAsync();

        // The original, accented text, with the match marked — not the folded form the index holds.
        Assert.Equal($"Első {FullText.MatchStart}kérés{FullText.MatchEnd}", hit.Title);
        Assert.False(string.IsNullOrEmpty(hit.Body));
    }

    [SkippableFact]
    public async Task A_snippet_takes_one_word_or_a_count_held_in_a_variable()
    {
        // ts_headline wants MinWords below MaxWords, and SQLite accepts any count, clamped; so must this.
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using var db = Unicode();
        var words = 5;

        var one = await db.Articles.Search("sqlite").Select(a => FullText.Snippet(a.Body, 1)).FirstAsync();
        var many = await db.Articles.Search("sqlite").Select(a => FullText.Snippet(a.Body, words)).FirstAsync();

        Assert.Contains(FullText.MatchStart.ToString(), one, StringComparison.Ordinal);
        Assert.Contains(FullText.MatchStart.ToString(), many, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task An_empty_search_is_no_filter_at_all()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using var db = Unicode();

        Assert.Equal(4, await db.Articles.Search("  ").CountAsync());
    }

    [SkippableFact]
    public async Task Adding_the_declaration_to_an_existing_table_is_a_migration_that_fills_the_index()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using (var plain = Plain())
        {
            await Postgres.ResetSchemaAsync(plain, SearchDbContext.Schema);
            plain.Articles.Add(new SearchArticle { Id = 1, Title = "Already here", Body = "sqlite before the index" });
            await plain.SaveChangesAsync();
        }

        await using var db = Unicode();
        await MigrateAsync(db, from: Plain());

        // A generated column is computed for the rows already in the table, so nothing needs a backfill.
        Assert.Equal([1], await db.Articles.Search("sqlite").Select(a => a.Id).ToListAsync());
    }

    [SkippableFact]
    public async Task Removing_the_declaration_drops_the_column_and_its_index()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await using var plain = Plain();
        await MigrateAsync(plain, from: Unicode());

        var columns = await plain.Database
            .SqlQueryRaw<string>(
                "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = {0} AND table_name = 'Articles'",
                SearchDbContext.Schema)
            .ToListAsync();
        Assert.DoesNotContain("RaskSearchVector", columns);
    }

    private static SearchDbContext Unicode() =>
        new(new DbContextOptionsBuilder<SearchDbContext>().UseRaskPostgresAt(Postgres.Required).Options);

    private static EnglishSearchDbContext English() =>
        new(new DbContextOptionsBuilder<EnglishSearchDbContext>().UseRaskPostgresAt(Postgres.Required).Options);

    private static PlainSearchDbContext Plain() =>
        new(new DbContextOptionsBuilder<PlainSearchDbContext>().UseRaskPostgresAt(Postgres.Required).Options);

    // The path `dotnet ef database update` takes: the differ between the two models, then the generator.
    private static async Task MigrateAsync(DbContext context, DbContext from)
    {
        await using (from)
        {
            var target = context.GetService<IDesignTimeModel>().Model;
            var source = from.GetService<IDesignTimeModel>().Model.GetRelationalModel();
            var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(source, target.GetRelationalModel());
            foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(operations, target))
            {
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            }
        }
    }
}
