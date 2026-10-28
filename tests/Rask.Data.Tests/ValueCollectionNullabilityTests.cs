using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Data.Tests.Shapes;

namespace Rask.Data.Tests;

/// <summary>
/// A collection of values is NULL in the database exactly where the model says it may be null: a non-nullable
/// <c>IReadOnlyList&lt;FiledFile&gt;</c> is a NOT NULL JSON column, a nullable one a NULL column, and the plain
/// collections beside them follow the same rule.
/// </summary>
/// <remarks>
/// The JSON one used to be NULL whatever the model said: it was an owned collection, whose column EF Core makes
/// nullable and refuses to be told otherwise ("cannot be configured as required since it was configured as a
/// collection"). An app adopting a <c>Files nvarchar(max) NOT NULL</c> column had no way to say so.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class ValueCollectionNullabilityTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-filings-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public void A_column_is_null_only_where_the_model_says_the_collection_may_be()
    {
        using var context = new FilingContext(Sqlite<FilingContext>());

        var columns = ValueObjectMappingDiff.Columns(context, ReturnFiling.Table);

        Assert.Equal(
            [
                "Aliases TEXT NULL default=-",
                "Attachments TEXT NULL default=-",
                "Files TEXT NOT NULL default=-",
                "Id INTEGER NOT NULL default=-",
                "Labels TEXT NOT NULL default=-",
            ],
            columns);
    }

    [Fact]
    public void The_create_script_says_so_too()
    {
        using var context = new FilingContext(Sqlite<FilingContext>());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Files\" TEXT NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"Attachments\" TEXT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"Labels\" TEXT NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"Aliases\" TEXT NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_object_collection_is_a_complex_collection_stored_as_json()
    {
        using var context = new FilingContext(Sqlite<FilingContext>());
        var filing = context.Model.FindEntityType(typeof(ReturnFiling))!;

        var files = filing.FindComplexProperty(nameof(ReturnFiling.Files))!;

        Assert.True(files.IsCollection);
        Assert.False(files.IsNullable);
        Assert.Equal("Files", files.ComplexType.GetContainerColumnName());
        Assert.True(filing.FindComplexProperty(nameof(ReturnFiling.Attachments))!.IsNullable);
        Assert.Null(filing.FindNavigation(nameof(ReturnFiling.Files)));
    }

    [Fact]
    public void A_view_with_no_list_behind_it_keeps_the_owned_mapping_and_its_null_column()
    {
        using var context = new DraftFilingContext(Sqlite<DraftFilingContext>());
        var draft = context.Model.FindEntityType(typeof(DraftFiling))!;

        var files = draft.FindNavigation(nameof(DraftFiling.Files));
        var columns = ValueObjectMappingDiff.Columns(context, nameof(DraftFiling));

        Assert.True(files!.TargetEntityType.IsMappedToJson());
        Assert.Contains("Files TEXT NULL default=-", columns);
    }

    [Fact]
    public async Task An_empty_collection_is_stored_as_an_empty_array_on_insert_and_on_update()
    {
        await using var context = await CreatedAsync();
        var filing = ReturnFiling.Empty();
        context.Add(filing);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var inserted = await StoredAsync(context, filing.Id);

        filing.File("return.pdf", 3);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        filing.Withdraw();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var updated = await StoredAsync(context, filing.Id);

        Assert.Equal(("'[]'", "NULL", "'[]'", "NULL"), inserted);
        Assert.Equal(("'[]'", "NULL", "'[]'", "NULL"), updated);
    }

    [Fact]
    public async Task An_empty_array_loads_as_an_empty_list_and_a_null_column_as_null()
    {
        await using (var context = await CreatedAsync())
        {
            context.Add(ReturnFiling.Empty());
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var fresh = new FilingContext(Sqlite<FilingContext>());
        var loaded = await fresh.Set<ReturnFiling>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Empty(loaded.Files);
        Assert.Empty(loaded.Labels);
        Assert.Null(loaded.Attachments);
        Assert.Null(loaded.Aliases);
    }

    [Fact]
    public async Task What_a_domain_method_files_round_trips_and_is_queried_in_sql()
    {
        await using (var context = await CreatedAsync())
        {
            var filing = ReturnFiling.Empty();
            filing.File("return.pdf", 3);
            filing.File("annex.pdf", 12);
            context.AddRange(filing, ReturnFiling.Empty());
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var fresh = new FilingContext(Sqlite<FilingContext>());
        var thick = await fresh.Set<ReturnFiling>()
            .Where(f => f.Files.Any(file => file.Pages > 10))
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal([("return.pdf", 3), ("annex.pdf", 12)], thick.Files.Select(f => (f.Name, f.Pages)));
    }

    [Fact]
    public async Task The_read_face_reads_an_empty_array_as_an_empty_list()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);
        var filing = ReturnFiling.Empty();
        filing.Attach("receipt.pdf");
        var empty = ReturnFiling.Empty();
        database.Context.AddRange(filing, empty);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var read = await ReturnFiling.Where(f => f.Id == filing.Id).Single(TestContext.Current.CancellationToken);
        var none = await ReturnFiling.Where(f => f.Id == empty.Id).Single(TestContext.Current.CancellationToken);

        Assert.Equal(["receipt.pdf"], read!.Attachments.Select(a => a.Name));
        Assert.Empty(none!.Labels);

        // The face holds a list where the model holds a nullable one: a NULL column reads as no items.
        Assert.Empty(none.Files);
        Assert.Empty(none.Attachments ?? []);
        Assert.Empty(none.Aliases ?? []);
    }

    [Fact]
    public void An_adopted_not_null_json_column_is_the_column_rask_maps()
    {
        using var legacy = new LegacyFilingContext(Sqlite<LegacyFilingContext>());
        using var rask = new FilingContext(Sqlite<FilingContext>());

        var (forward, back) = ValueObjectMappingDiff.Between(legacy, rask);

        Assert.Empty(forward);
        Assert.Empty(back);
    }

    [Fact]
    public void An_app_on_the_owned_mapping_gets_one_alter_column_that_fills_with_an_empty_array()
    {
        using var owned = new OwnedFilingContext(Sqlite<OwnedFilingContext>());
        using var rask = new FilingContext(Sqlite<FilingContext>());

        var (forward, _) = ValueObjectMappingDiff.Between(owned, rask);
        var alter = Assert.Single(Differences(owned, rask).OfType<AlterColumnOperation>());

        Assert.Equal(["AlterColumn ReturnFilings.Files: TEXT NULL -> TEXT NOT NULL"], forward);

        // EF Core's own differ writes "{}" here — an object, which no collection can read back.
        Assert.Equal("[]", alter.DefaultValue);
    }

    [Fact]
    public async Task The_migration_rask_generates_fills_the_null_rows_with_an_empty_array()
    {
        await SeedOnTheOwnedMappingAsync();
        await using var rask = new FilingContext(Sqlite<FilingContext>());

        await MigrateFromOwnedAsync(rask);
        var filings = await rask.Set<ReturnFiling>().OrderBy(f => f.Id).ToListAsync(TestContext.Current.CancellationToken);
        var stored = await StoredAsync(rask, filings[0].Id);

        Assert.Equal([0, 1], filings.Select(f => f.Files.Count));
        Assert.Equal("'[]'", stored.Files);
    }

    [Fact]
    public void A_json_column_holding_one_value_object_keeps_an_empty_object()
    {
        using var owned = new CoverContext(Sqlite<CoverContext>(), complex: false);
        using var complex = new CoverContext(Sqlite<CoverContext>(), complex: true);

        var alter = Assert.Single(Differences(owned, complex).OfType<AlterColumnOperation>());

        Assert.Equal("Cover", alter.Name);
        Assert.Equal("{}", alter.DefaultValue);
    }

    private async Task SeedOnTheOwnedMappingAsync()
    {
        await using var owned = new OwnedFilingContext(Sqlite<OwnedFilingContext>());
        await owned.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await owned.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var insert = owned.Database.GetDbConnection().CreateCommand();
        insert.CommandText = "INSERT INTO \"ReturnFilings\" (\"Files\", \"Labels\") VALUES (NULL, '[]'), ('[{\"Name\":\"a.pdf\",\"Pages\":1}]', '[]')";
        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    // The operations `dotnet ef migrations add` writes, applied the way `dotnet ef database update` applies them.
    private async Task MigrateFromOwnedAsync(FilingContext rask)
    {
        await using var owned = new OwnedFilingContext(Sqlite<OwnedFilingContext>());
        var operations = Differences(owned, rask);

        var commands = rask.GetService<IMigrationsSqlGenerator>().Generate(operations, rask.GetService<IDesignTimeModel>().Model);
        await rask.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(
            commands, rask.GetService<IRelationalConnection>(), TestContext.Current.CancellationToken);
    }

    private static List<MigrationOperation> Differences(DbContext from, DbContext to) =>
        [.. to.GetService<IMigrationsModelDiffer>().GetDifferences(
            from.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            to.GetService<IDesignTimeModel>().Model.GetRelationalModel())];

    private async Task<FilingContext> CreatedAsync()
    {
        var context = new FilingContext(Sqlite<FilingContext>());
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        return context;
    }

    private static async Task<(string Files, string Attachments, string Labels, string Aliases)> StoredAsync(DbContext context, int id)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT quote(\"Files\"), quote(\"Attachments\"), quote(\"Labels\"), quote(\"Aliases\") FROM \"ReturnFilings\" WHERE \"Id\" = {id}";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    private DbContextOptions<TContext> Sqlite<TContext>()
        where TContext : DbContext => Sqlite<TContext>($"Data Source={_dbPath}");

    // Through Rask's differ, which UseRaskSqlite, UseRaskSqlServer, UseRaskPostgres and a RaskApp register; the wiring
    // is pinned in their own test projects, so here it is put in place directly.
    private static DbContextOptions<TContext> Sqlite<TContext>(string connectionString)
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>()
        .UseSqlite(connectionString)
        .ReplaceService<IMigrationsModelDiffer, RaskMigrationsModelDiffer>()
        .Options;
}

/// <summary>A row holding ONE value object as JSON — the column EF Core's "{}" is right for.</summary>
public sealed class CoverRow
{
    public int Id { get; set; }

    public FiledFile Cover { get; set; } = new("", 0);
}

/// <summary>The row with its cover owned (a NULL column) or complex (NOT NULL), so the move between them is diffed.</summary>
public sealed class CoverContext(DbContextOptions<CoverContext> options, bool complex) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, CoverModelKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<CoverRow>(b =>
        {
            if (complex)
            {
                b.ComplexProperty(r => r.Cover, c => c.ToJson());
            }
            else
            {
                b.OwnsOne(r => r.Cover, o => o.ToJson());
                b.Navigation(r => r.Cover).IsRequired(false);
            }
        });

    internal bool Complex => complex;

    private sealed class CoverModelKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((CoverContext)context).Complex, designTime);
    }
}
