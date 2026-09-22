using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Integration tests for HasFullTextSearch + Search, driven through the same migration path `dotnet ef` uses (model
// differ -> IMigrationsSqlGenerator) against a real SQLite database file, with Rask's production pragmas — notably
// trusted_schema=OFF, which the sync triggers have to run under.
public sealed class FullTextSearchTests : IDisposable
{
    private const char S = FullText.MatchStart;
    private const char E = FullText.MatchEnd;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-fts-{Guid.NewGuid():N}.db");

    // ---- Querying ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Search_returns_matching_rows_best_match_first()
    {
        await using var db = await SeededAsync();

        var titles = await db.Articles.Search("sqlite").Select(a => a.Title).ToListAsync();

        // The article saying "sqlite" three times in a short body outranks the one mentioning it once in a long one.
        Assert.Equal(["All about SQLite", "Databases"], titles);
    }

    [Fact]
    public async Task Every_word_must_match_in_any_order_and_any_column()
    {
        await using var db = await SeededAsync();

        Assert.Equal(["All about SQLite"], await Titles(db.Articles.Search("fast sqlite")));
        Assert.Empty(await Titles(db.Articles.Search("sqlite kitten")));
    }

    [Fact]
    public async Task Case_and_diacritics_are_ignored()
    {
        await using var db = await SeededAsync();

        Assert.Equal(["Első kérés"], await Titles(db.Articles.Search("KERES")));
        Assert.Equal(["Első kérés"], await Titles(db.Articles.Search("első")));
    }

    [Fact]
    public async Task The_last_word_matches_as_a_prefix_so_results_narrow_while_typing()
    {
        await using var db = await SeededAsync();

        Assert.Equal(["All about SQLite", "Databases"], await Titles(db.Articles.Search("sql")));
        Assert.Equal(["All about SQLite"], await Titles(db.Articles.Search("fast sql")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- ** \"\" ()")]
    public async Task Text_with_no_word_filters_nothing(string? text)
    {
        await using var db = await SeededAsync();

        Assert.Equal(await db.Articles.CountAsync(), await db.Articles.Search(text).CountAsync());
    }

    [Theory]
    [InlineData("\"sqlite")]
    [InlineData("sqlite\"")]
    [InlineData("sqlite*")]
    [InlineData("*sqlite")]
    [InlineData("NEAR(sqlite fast)")]
    [InlineData("title:sqlite")]
    [InlineData("sqlite OR kitten")]
    [InlineData("sqlite NOT fast")]
    [InlineData("-sqlite")]
    [InlineData("^sqlite")]
    [InlineData("(sqlite")]
    [InlineData("sqlite'; DROP TABLE Articles; --")]
    public async Task Query_syntax_in_the_text_is_just_text(string text)
    {
        await using var db = await SeededAsync();

        // Never a syntax error, and never an operator: whatever it matches, the table is still there.
        _ = await db.Articles.Search(text).ToListAsync();
        Assert.Equal(4, await db.Articles.CountAsync());
    }

    [Fact]
    public async Task Operators_are_matched_as_words_not_obeyed()
    {
        await using var db = await SeededAsync();

        // As FTS5 syntax this would be "sqlite OR kitten" and match two rows; as words, no row has all three.
        Assert.Empty(await Titles(db.Articles.Search("sqlite OR kitten")));
    }

    [Fact]
    public async Task A_search_still_composes_with_Where_and_Skip_and_Take_and_Count()
    {
        await using var db = await SeededAsync();

        Assert.Equal(["Databases"], await Titles(db.Articles.Search("sqlite").Where(a => a.Published)));
        Assert.Equal(["Databases"], await Titles(db.Articles.Search("sqlite").Skip(1).Take(1)));
        Assert.Equal(2, await db.Articles.Search("sqlite").CountAsync());
        Assert.True(await db.Articles.Search("sqlite").AnyAsync());
    }

    [Fact]
    public async Task A_later_OrderBy_replaces_best_match_order()
    {
        await using var db = await SeededAsync();

        var ordered = await db.Articles.Search("sqlite").OrderByDescending(a => a.Title).Select(a => a.Title).ToListAsync();

        Assert.Equal(["Databases", "All about SQLite"], ordered);
    }

    [Fact]
    public async Task A_derived_type_searches_the_index_its_base_declares()
    {
        await using var db = Create<PageContext>();
        TestMigrations.Apply(db);
        db.Pages.AddRange(
            new Page { Id = 1, Text = "plain page about search" },
            new HelpPage { Id = 2, Text = "help page about search", Topic = "t" });
        await db.SaveChangesAsync();

        var help = await db.Set<Page>().OfType<HelpPage>().Search("search")
            .Select(p => new { p.Id, Text = FullText.Highlight(p.Text) })
            .ToListAsync();
        var all = await db.Pages.Search("search").CountAsync();

        var hit = Assert.Single(help);
        Assert.Equal(2, hit.Id);
        Assert.Equal($"help page about {S}search{E}", hit.Text);
        Assert.Equal(2, all);
    }

    [Fact]
    public async Task A_query_filter_still_applies_to_the_matches()
    {
        await using var db = Create<PublishedArticleContext>();
        TestMigrations.Apply(db);
        await SeedAsync(db);

        Assert.Equal(["Databases"], await Titles(db.Articles.Search("sqlite")));
    }

    [Fact]
    public async Task The_plan_scans_the_index_and_seeks_the_table()
    {
        await using var db = await SeededAsync();

        var plan = await PlanAsync(db, db.Articles.Search("sqlite").Take(10).ToQueryString());

        Assert.Contains("VIRTUAL TABLE INDEX", plan, StringComparison.Ordinal);
        Assert.Contains("USING INTEGER PRIMARY KEY", plan, StringComparison.Ordinal);
        // The only full scan is of the index itself; the table is reached by key, never scanned.
        Assert.All(
            plan.Split('\n').Where(step => step.StartsWith("SCAN ", StringComparison.Ordinal)),
            step => Assert.Contains("VIRTUAL TABLE INDEX", step, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Highlight_marks_every_matched_term_in_the_whole_value()
    {
        await using var db = await SeededAsync();

        var hit = await db.Articles.Search("fast sqlite")
            .Select(a => new { a.Id, Title = FullText.Highlight(a.Title), Body = FullText.Highlight(a.Body) })
            .SingleAsync();

        Assert.Equal($"All about {S}SQLite{E}", hit.Title);
        Assert.Equal($"{S}SQLite{E} is small, {S}SQLite{E} is {S}fast{E}, {S}SQLite{E} is everywhere.", hit.Body);
    }

    [Fact]
    public async Task Snippet_cuts_around_the_match_and_marks_it()
    {
        await using var db = await SeededAsync();

        var snippet = await db.Articles.Search("postgres")
            .Select(a => FullText.Snippet(a.Body, 4))
            .SingleAsync();

        Assert.Equal($"…like {S}Postgres{E} and SQLite…", snippet);
    }

    [Fact]
    public async Task Snippet_words_out_of_range_are_clamped_rather_than_failing_the_query()
    {
        await using var db = await SeededAsync();
        var words = 1000;

        var snippet = await db.Articles.Search("postgres").Select(a => FullText.Snippet(a.Body, words)).SingleAsync();

        Assert.Contains($"{S}Postgres{E}", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Highlight_without_Search_says_how_to_use_it()
    {
        await using var db = await SeededAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Articles.Select(a => FullText.Highlight(a.Title)).ToListAsync());

        Assert.Contains("Article.Search(text).Select(p => FullText.Highlight(p.Title))", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Highlight_of_a_property_that_is_not_indexed_names_the_indexed_ones()
    {
        await using var db = await SeededAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Articles.Search("sqlite").Select(a => FullText.Highlight(a.Tag)).ToListAsync());

        Assert.Contains("Title, Body", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Searching_an_entity_without_an_index_says_how_to_declare_one()
    {
        await using var db = Create<PlainArticleContext>();
        TestMigrations.Apply(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Articles.Search("sqlite").ToListAsync());

        Assert.Contains("HasFullTextSearch", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuring_the_context_twice_still_rewrites_once()
    {
        var options = new DbContextOptionsBuilder<ArticleContext>()
            .UseRaskSqliteAt($"Data Source={_dbPath}")
            .UseRaskSqliteAt($"Data Source={_dbPath}")
            .Options;

        await using var db = new ArticleContext(options);
        TestMigrations.Apply(db);
        await SeedAsync(db);

        Assert.Equal(["All about SQLite", "Databases"], await Titles(db.Articles.Search("sqlite")));
    }

    [Fact]
    public async Task English_stemming_matches_other_forms_of_a_word()
    {
        await using var db = Create<EnglishArticleContext>();
        TestMigrations.Apply(db);
        db.Articles.Add(new Article { Id = 1, Title = "Running", Body = "She runs every day." });
        await db.SaveChangesAsync();

        Assert.Equal(["Running"], await Titles(db.Articles.Search("run")));
    }

    // ---- Keeping the index current ----------------------------------------------------------------------------

    [Fact]
    public async Task Changes_through_EF_are_searchable_immediately()
    {
        await using var db = await SeededAsync();

        var article = await db.Articles.SingleAsync(a => a.Title == "Databases");
        article.Body = "Now about zebras.";
        db.Articles.Remove(await db.Articles.SingleAsync(a => a.Title == "All about SQLite"));
        await db.SaveChangesAsync();

        Assert.Empty(await Titles(db.Articles.Search("sqlite")));
        Assert.Equal(["Databases"], await Titles(db.Articles.Search("zebras")));
    }

    [Fact]
    public async Task Writes_that_bypass_EF_are_searchable_too()
    {
        await using var db = await SeededAsync();

        await db.Database.ExecuteSqlRawAsync("""INSERT INTO "Articles" ("Id", "Title", "Body", "Published") VALUES (10, 'Raw', 'written by hand', 0);""");
        await db.Database.ExecuteSqlRawAsync("""UPDATE "Articles" SET "Body" = 'zebras only' WHERE "Title" = 'Databases';""");
        await db.Articles.Where(a => a.Title == "All about SQLite").ExecuteDeleteAsync();

        Assert.Equal(["Raw"], await Titles(db.Articles.Search("hand")));
        Assert.Equal(["Databases"], await Titles(db.Articles.Search("zebras")));
        Assert.Empty(await Titles(db.Articles.Search("sqlite")));
        await AssertIntegrityAsync(db);
    }

    [Fact]
    public async Task Insert_or_replace_and_upsert_keep_the_index_right()
    {
        // A REPLACE deletes the old row without firing AFTER DELETE (recursive_triggers is off), so an index that could
        // only forget a row by its old values would keep "SQLite" for row 1 forever.
        await using var db = await SeededAsync();

        await db.Database.ExecuteSqlRawAsync(
            """INSERT OR REPLACE INTO "Articles" ("Id", "Title", "Body", "Published") VALUES (1, 'Replaced', 'zebras now', 0);""");
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "Articles" ("Id", "Title", "Body", "Published") VALUES (3, 'Kittens', 'upserted giraffes', 0) ON CONFLICT ("Id") DO UPDATE SET "Body" = excluded."Body";""");

        Assert.Equal(["Databases"], await Titles(db.Articles.Search("sqlite")));
        Assert.Equal(["Replaced"], await Titles(db.Articles.Search("zebras")));
        Assert.Equal(["Kittens"], await Titles(db.Articles.Search("giraffes")));
        Assert.Empty(await Titles(db.Articles.Search("storage")));
        Assert.Equal(4, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS Value FROM "Articles_fts" """).SingleAsync());
        await AssertIntegrityAsync(db);
    }

    [Fact]
    public async Task A_Guid_key_survives_insert_or_replace()
    {
        await using var db = Create<MemoContext>();
        TestMigrations.Apply(db);
        var id = Guid.NewGuid();
        db.Memos.Add(new Memo { Id = id, Text = "buy milk" });
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlAsync(
            $"""INSERT OR REPLACE INTO "Memos" ("Id", "Text") VALUES ({id}, 'sell bread')""");

        Assert.Empty(await db.Memos.Search("milk").ToListAsync());
        Assert.Equal(id, (await db.Memos.Search("bread").SingleAsync()).Id);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS Value FROM "Memos_fts" """).SingleAsync());
    }

    [Theory]
    [InlineData(typeof(TicketContext))]
    [InlineData(typeof(ConventionTicketContext))]
    public async Task A_converted_integer_key_is_the_rowid_on_both_sides(Type contextType)
    {
        var options = (DbContextOptions)typeof(FullTextSearchTests)
            .GetMethod(nameof(Options), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(contextType)
            .Invoke(this, [false])!;
        await using var db = (DbContext)Activator.CreateInstance(contextType, options)!;
        TestMigrations.Apply(db);

        db.Set<Ticket>().AddRange(new Ticket { Id = new TicketId(7), Text = "printer on fire" }, new Ticket { Id = new TicketId(8), Text = "fine" });
        await db.SaveChangesAsync();

        // The migration saved with the model records the choice, so an older saved model cannot disagree with the query.
        Assert.Equal("rowid", db.Model.FindEntityType(typeof(Ticket))!.FindAnnotation("Rask:FullTextSearch:Layout")?.Value);
        Assert.DoesNotContain("Tickets_fts_keys", TestMigrations.Ddl(db), StringComparison.Ordinal);

        var hit = await db.Set<Ticket>().Search("fire").Select(t => new { t.Id, Text = FullText.Highlight(t.Text) }).SingleAsync();
        Assert.Equal(new TicketId(7), hit.Id);
        Assert.Equal($"printer on {S}fire{E}", hit.Text);
    }

    [Fact]
    public async Task A_recorded_layout_wins_over_the_rule_for_the_migration_and_the_query()
    {
        await using var db = Create<RecordedKeyMapArticleContext>();
        TestMigrations.Apply(db);
        await SeedAsync(db);

        Assert.Contains("Articles_fts_keys", TestMigrations.Ddl(db), StringComparison.Ordinal);
        Assert.Equal(["All about SQLite", "Databases"], await Titles(db.Articles.Search("sqlite")));
    }

    // ---- Migrations -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Adding_search_to_an_existing_table_is_a_migration_that_fills_the_index()
    {
        await using (var before = Create<PlainArticleContext>())
        {
            TestMigrations.Apply(before);
            await SeedAsync(before);
        }

        await using var after = Create<ArticleContext>();
        var operations = Migrate<PlainArticleContext>(after);

        // Without the table annotation the differ would see no change at all, and the index would never exist.
        Assert.Contains(operations, o => o is AlterTableOperation { Name: "Articles" });
        Assert.Equal(["All about SQLite", "Databases"], await Titles(after.Articles.Search("sqlite")));
        Assert.Equal(3, TestMigrations.TriggerCount(after));
    }

    [Fact]
    public async Task Removing_search_drops_the_index_and_its_triggers()
    {
        await using (var before = await SeededAsync())
        {
        }

        await using var after = Create<PlainArticleContext>();
        Migrate<ArticleContext>(after);

        Assert.DoesNotContain("Articles_fts", TestMigrations.Ddl(after), StringComparison.Ordinal);
        Assert.Equal(0, TestMigrations.TriggerCount(after));

        // And writes no longer reach for an index that is gone.
        after.Articles.Add(new Article { Id = 20, Title = "After", Body = "no index" });
        await after.SaveChangesAsync();
    }

    [Fact]
    public async Task Changing_the_indexed_properties_rebuilds_the_index()
    {
        await using (var before = await SeededAsync())
        {
        }

        await using var after = Create<TitleOnlyArticleContext>();
        Migrate<ArticleContext>(after);

        // "fast" was only ever in a body.
        Assert.Empty(await Titles(after.Articles.Search("fast")));
        Assert.Equal(["All about SQLite"], await Titles(after.Articles.Search("sqlite")));
    }

    [Fact]
    public async Task The_index_survives_a_migration_that_rebuilds_the_table()
    {
        await using (var before = await SeededAsync())
        {
        }

        await using var after = Create<RebuiltArticleContext>();
        Migrate<ArticleContext>(after);

        Assert.Equal(3, TestMigrations.TriggerCount(after));
        Assert.Equal(["All about SQLite", "Databases"], await Titles(after.Articles.Search("sqlite")));

        after.Articles.Add(new Article { Id = 30, Title = "New", Body = "sqlite again and again and again", Tag = "x" });
        await after.SaveChangesAsync();
        Assert.Contains("New", await Titles(after.Articles.Search("sqlite")));
    }

    [Fact]
    public async Task The_index_follows_a_renamed_column()
    {
        await using (var before = await SeededAsync())
        {
        }

        await using var after = Create<RenamedArticleContext>();
        Migrate<ArticleContext>(after);

        Assert.Equal(["All about SQLite"], await Titles(after.Articles.Search("fast")));
        Assert.Equal($"{S}SQLite{E} is small, {S}SQLite{E} is fast, {S}SQLite{E} is everywhere.",
            await after.Articles.Search("sqlite").Select(a => FullText.Highlight(a.Body)).FirstAsync());
    }

    [Fact]
    public async Task Strict_tables_and_full_text_search_hold_at_the_same_time()
    {
        await using var db = Create<ArticleContext>(strictTables: true);
        TestMigrations.Apply(db);
        await SeedAsync(db);

        Assert.Contains("STRICT", TestMigrations.Ddl(db), StringComparison.Ordinal);
        Assert.Equal(["All about SQLite", "Databases"], await Titles(db.Articles.Search("sqlite")));
    }

    [Fact]
    public async Task The_full_text_index_survives_a_VACUUM()
    {
        await using var db = await SeededAsync();

        await db.Database.ExecuteSqlRawAsync("VACUUM;");

        Assert.Equal(["All about SQLite", "Databases"], await Titles(db.Articles.Search("sqlite")));
        await AssertIntegrityAsync(db);
    }

    // ---- Keys that are not a rowid ----------------------------------------------------------------------------

    [Fact]
    public async Task A_Guid_key_is_searched_through_its_key_map()
    {
        await using var db = Create<MemoContext>();
        TestMigrations.Apply(db);

        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        db.Memos.AddRange(new Memo { Id = keep, Text = "buy milk" }, new Memo { Id = drop, Text = "buy bread" });
        await db.SaveChangesAsync();

        db.Memos.Remove(await db.Memos.SingleAsync(m => m.Id == drop));
        (await db.Memos.SingleAsync(m => m.Id == keep)).Text = "buy oat milk";
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("VACUUM;");

        var hit = await db.Memos.Search("buy").Select(m => new { m.Id, Text = FullText.Highlight(m.Text) }).SingleAsync();
        Assert.Equal(keep, hit.Id);
        Assert.Equal($"{S}buy{E} oat milk", hit.Text);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS Value FROM "Memos_fts_keys" """).SingleAsync());

        var plan = await PlanAsync(db, db.Memos.Search("buy").ToQueryString());
        Assert.Contains("VIRTUAL TABLE INDEX", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Guid_key_table_that_already_has_rows_is_filled()
    {
        await using (var before = new PlainMemoContext(Options<PlainMemoContext>()))
        {
            TestMigrations.Apply(before);
            before.Set<Memo>().Add(new Memo { Id = Guid.NewGuid(), Text = "already here" });
            await before.SaveChangesAsync();
        }

        await using var after = Create<MemoContext>();
        Migrate<PlainMemoContext>(after);

        Assert.Single(await after.Memos.Search("already").ToListAsync());
        Assert.Equal(1, await after.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS Value FROM "Memos_fts_keys" """).SingleAsync());
    }

    [Fact]
    public async Task A_composite_key_is_searched_through_its_key_map()
    {
        await using var db = Create<EntryContext>();
        TestMigrations.Apply(db);

        db.Entries.AddRange(
            new Entry { TenantId = 1, Code = "a", Text = "alpha shared" },
            new Entry { TenantId = 2, Code = "a", Text = "beta shared" });
        await db.SaveChangesAsync();

        var hits = await db.Entries.Search("shared").Where(e => e.TenantId == 2)
            .Select(e => new { e.TenantId, e.Code, Text = FullText.Highlight(e.Text) })
            .ToListAsync();

        var hit = Assert.Single(hits);
        Assert.Equal((2, "a"), (hit.TenantId, hit.Code));
        Assert.Equal($"beta {S}shared{E}", hit.Text);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------

    private async Task<ArticleContext> SeededAsync()
    {
        var db = Create<ArticleContext>();
        TestMigrations.Apply(db);
        await SeedAsync(db);
        return db;
    }

    private static async Task SeedAsync(DbContext db)
    {
        db.Set<Article>().AddRange(
            new Article { Id = 1, Title = "All about SQLite", Body = "SQLite is small, SQLite is fast, SQLite is everywhere." },
            new Article
            {
                Id = 2,
                Title = "Databases",
                Body = "There are many databases like Postgres and SQLite and they all store rows in tables on disk somewhere.",
                Published = true,
            },
            new Article { Id = 3, Title = "Kittens", Body = "Nothing to do with storage." },
            new Article { Id = 4, Title = "Első kérés", Body = "Magyar szöveg." });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static Task<List<string>> Titles(IQueryable<Article> query) => query.Select(a => a.Title).ToListAsync();

    private static async Task<string> PlanAsync(DbContext db, string queryString)
    {
        // ToQueryString prefixes the SQL with `.param set` lines; the parameters are bound for real below.
        var lines = queryString.Split('\n');
        var sql = string.Join('\n', lines.Where(l => !l.StartsWith(".param", StringComparison.Ordinal)));

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;

        foreach (var line in lines.Where(l => l.StartsWith(".param set ", StringComparison.Ordinal)))
        {
            var parts = line[".param set ".Length..].Trim().Split(' ', 2);
            var value = parts[1].Trim();
            command.Parameters.AddWithValue(
                parts[0],
                value.StartsWith('\'') ? value[1..^1].Replace("''", "'", StringComparison.Ordinal) : long.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(3));
        }

        return string.Join('\n', plan);
    }

    // FTS5's own consistency check: fails the statement if the index disagrees with the rows it indexes.
    private static Task AssertIntegrityAsync(DbContext db) =>
        db.Database.ExecuteSqlRawAsync("""INSERT INTO "Articles_fts"("Articles_fts", rank) VALUES ('integrity-check', 1);""");

    private IReadOnlyList<MigrationOperation> Migrate<TFrom>(DbContext context)
        where TFrom : DbContext
    {
        using var from = (TFrom)Activator.CreateInstance(typeof(TFrom), Options<TFrom>())!;
        return TestMigrations.Apply(context, from);
    }

    private TContext Create<TContext>(bool strictTables = false)
        where TContext : DbContext
        => (TContext)Activator.CreateInstance(typeof(TContext), Options<TContext>(strictTables))!;

    private DbContextOptions<TContext> Options<TContext>(bool strictTables = false)
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>()
            .UseRaskSqliteAt($"Data Source={_dbPath}", o => o.StrictTables = strictTables)
            .Options;

    private sealed class PlainMemoContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Memo>();
    }
}
