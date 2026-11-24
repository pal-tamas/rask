using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

/// <summary><c>Computed(…)</c> on SQLite: a stored generated column, computing what the C# says.</summary>
[Collection(DataDbCollection.Name)]
public sealed class ComputedColumnTests
{
    [Fact]
    public void A_computed_column_is_a_stored_generated_column_in_the_schema()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"CountedAt\" AS (COALESCE(\"ModifiedPistol\", \"Pistol\")) STORED", script, StringComparison.Ordinal);
        Assert.Contains("AS ((CAST(\"Year\" AS TEXT) || '.' || CAST(\"Month\" AS TEXT))) STORED", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_keeps_the_SQL_and_forgets_the_declaration()
    {
        using var context = new NeutralContext(Options());

        var property = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PumpReading))!.FindProperty(nameof(PumpReading.CountedAt))!;

        Assert.Equal("COALESCE(\"ModifiedPistol\", \"Pistol\")", property.GetComputedColumnSql());
        Assert.True(property.GetIsStored());
        Assert.DoesNotContain(property.GetAnnotations(), a => a.Name.StartsWith("Rask:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_computed_column_reads_back_what_the_same_lambda_computes_in_C_sharp()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = new NeutralContext(Options(connection));
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        PumpReading[] rows =
        [
            new() { Pistol = 3, ModifiedPistol = null, Year = 2026, Month = 1, Note = null, Litres = 10, Kind = PumpKind.Pump },
            new() { Pistol = 3, ModifiedPistol = 3, Year = 2025, Month = 12, Note = "same", Litres = 60, Kind = PumpKind.Manual },
            new() { Pistol = 2, ModifiedPistol = 19, Year = 1999, Month = 7, Note = "", Litres = 49.5, Kind = PumpKind.Manual },
            new() { Pistol = -4, ModifiedPistol = 0, Year = 0, Month = -1, Note = "x", Litres = 0, Kind = PumpKind.Pump },
        ];

        context.AddRange(rows);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var read = await context.Set<PumpReading>().AsNoTracking().ToDictionaryAsync(r => r.Id, TestContext.Current.CancellationToken);
        foreach (var row in rows)
        {
            var stored = read[row.Id];
            Assert.Equal(PumpReading.CountedAtIs.Compile()(row), stored.CountedAt);
            Assert.Equal(PumpReading.NameIs.Compile()(row), stored.Name);
            Assert.Equal(PumpReading.LabelIs.Compile()(row), stored.Label);
            Assert.Equal(PumpReading.MovedIs.Compile()(row), stored.Moved);
            Assert.Equal(PumpReading.SamePistolIs.Compile()(row), stored.SamePistol);
            Assert.Equal(PumpReading.SpreadIs.Compile()(row), stored.Spread);
            Assert.Equal(PumpReading.SizeIs.Compile()(row), stored.Size);
        }
    }

    [Fact]
    public async Task A_save_reads_the_computed_value_back_into_the_entity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = new NeutralContext(Options(connection));
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var reading = new PumpReading { Pistol = 5, Year = 2026, Month = 11 };

        context.Add(reading);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, reading.CountedAt);
        Assert.Equal("2026.11", reading.Name);
    }

    [Fact]
    public void A_method_the_subset_does_not_take_fails_the_model_naming_it()
    {
        using var context = RefusedContext.Declaring(m => m.Entity<PumpReading>().Computed(r => r.Name, r => r.Note!.ToUpperInvariant()));

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("Computed(r => r.Name, r => r.Note.ToUpperInvariant()) on PumpReading", refused.Message, StringComparison.Ordinal);
        Assert.Contains("r.Note.ToUpperInvariant() is not one Rask writes as SQL", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_Value_off_a_nullable_fails_the_model_pointing_at_GetValueOrDefault()
    {
        using var context = RefusedContext.Declaring(m => m.Entity<PumpReading>().Computed(r => r.CountedAt, r => r.ModifiedPistol!.Value));

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("write ?? or GetValueOrDefault()", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Joining_a_double_into_text_fails_the_model_since_each_database_writes_it_its_own_way()
    {
        using var context = RefusedContext.Declaring(m => m.Entity<PumpReading>().Computed(r => r.Name, r => "L" + r.Litres));

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("joins a Double into text", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_property_outside_the_row_fails_the_model_instead_of_being_frozen_at_build_time()
    {
        using var context = RefusedContext.Declaring(m => m.Entity<StorageTank>().Computed(t => t.Size, t => t.Size + DateTime.Now.Year));

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("reads a property outside the row", refused.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions<NeutralContext> Options() =>
        OptionsLayout.LikeAddDbContext<NeutralContext>().UseSqlite("Data Source=:memory:").Options;

    private static DbContextOptions<NeutralContext> Options(SqliteConnection connection) =>
        OptionsLayout.LikeAddDbContext<NeutralContext>().UseSqlite(connection).Options;
}
