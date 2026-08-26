using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// SQLite has no decimal type, so EF Core stores one as invariant TEXT and emits
// `ORDER BY "x" COLLATE EF_DECIMAL` so that text sorts numerically. EF registers EF_DECIMAL as
// decimal.Compare(decimal.Parse(x), decimal.Parse(y)) — with no IFormatProvider — so it reads the
// invariant text under CurrentCulture. UseRaskSqlite re-registers the collation invariantly; these
// tests drive real files under real locales to prove it, and pin the upstream contract it depends on.
//
// The assembly disables parallelisation (see AssemblyInfo.cs), so swapping CurrentCulture is safe here.
public sealed class RaskSqliteDecimalOrderingTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"rask-sqlite-decimal-{Guid.NewGuid():N}.db");

    // Deliberately mixes digit counts so a lexicographic sort differs from a numeric one ("10.00" < "9.50"),
    // and includes a value whose '.' is in group-separator position for de-DE ("19.95" -> 1995).
    private static readonly decimal[] Unsorted = [19.95m, 2.00m, 100.50m, 9.50m, 10.00m];
    private static readonly decimal[] Ascending = [2.00m, 9.50m, 10.00m, 19.95m, 100.50m];

    [Theory]
    [InlineData("")]        // invariant
    [InlineData("en-US")]   // '.' is the decimal separator — the happy path
    [InlineData("de-DE")]   // '.' is the GROUP separator: EF's collation silently mis-parses 19.95 as 1995
    [InlineData("fr-FR")]   // ',' decimal separator
    [InlineData("en-HU")]   // '.' is neither separator: EF's collation THROWS, killing the process
    [InlineData("hu-HU")]
    public async Task Ordering_a_decimal_column_is_numeric_on_every_locale(string culture)
    {
        await SeedAsync();

        await WithCultureAsync(culture, async () =>
        {
            await using var context = NewContext();
            var ordered = await context.Rows.OrderBy(r => r.Amount).Select(r => r.Amount).ToListAsync();
            Assert.Equal(Ascending, ordered);
        });
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-HU")]
    public async Task Grouping_and_distinct_on_a_decimal_column_are_numeric_on_every_locale(string culture)
    {
        await SeedAsync();

        await WithCultureAsync(culture, async () =>
        {
            await using var context = NewContext();

            var distinct = await context.Rows
                .Select(r => r.Amount).Distinct().OrderBy(a => a).ToListAsync();
            Assert.Equal(Ascending, distinct);

            var grouped = await context.Rows
                .GroupBy(r => r.Amount).Select(g => g.Key).OrderBy(k => k).ToListAsync();
            Assert.Equal(Ascending, grouped);
        });
    }

    // The value on disk must be invariant regardless of the writing machine's locale — '.' never ','.
    // If this ever regressed, the collation would be comparing text nobody can parse back.
    [Theory]
    [InlineData("")]
    [InlineData("de-DE")]
    [InlineData("en-HU")]
    [InlineData("fr-FR")]
    public async Task A_decimal_is_stored_as_invariant_text_on_every_locale(string culture)
    {
        await WithCultureAsync(culture, async () =>
        {
            await using (var context = NewContext())
            {
                await context.Database.EnsureCreatedAsync();
                context.Rows.Add(new ProbeRow { Amount = 19.95m });
                context.Rows.Add(new ProbeRow { Amount = -1234567.89m });
                await context.SaveChangesAsync();
            }

            var stored = ReadRawAmounts();
            Assert.Equal(["19.95", "-1234567.89"], stored);
            Assert.All(stored, s => Assert.DoesNotContain(',', s));
        });
    }

    // SQLite is dynamically typed: any text can land in a decimal column via a direct INSERT, an
    // external tool or a legacy row. EF's collation throws on it — and because the throw happens inside
    // a native SQLite callback it cannot be unwound, so it takes the whole process down rather than
    // surfacing as a query error. Rask's collation orders it instead.
    [Fact]
    public async Task Ordering_survives_non_numeric_text_in_a_decimal_column()
    {
        await SeedAsync();

        await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO Rows (Amount) VALUES ('lots')";
            await insert.ExecuteNonQueryAsync();
        }

        // Order by the decimal but project the key: SQLite still runs the collation over every row,
        // including the junk one, without EF having to materialise 'lots' as a decimal.
        await using var context = NewContext();
        var orderedIds = await context.Rows.OrderBy(r => r.Amount).Select(r => r.Id).ToListAsync();

        // Seeded in Unsorted order, so Id 1..5 are 19.95, 2.00, 100.50, 9.50, 10.00 and Id 6 is 'lots'.
        // Numbers keep their numeric order; unparseable text sorts after all of them, deterministically.
        Assert.Equal([2, 4, 5, 1, 3, 6], orderedIds);
    }

    // The collation is re-registered on every open because Microsoft.Data.Sqlite's pool runs
    // Deactivate() on return, which un-registers functions and collations. Churn the pool to prove a
    // recycled connection still orders correctly.
    [Fact]
    public async Task The_collation_survives_pooled_connection_reuse()
    {
        await SeedAsync();

        await WithCultureAsync("en-HU", async () =>
        {
            for (var i = 0; i < 25; i++)
            {
                await using var context = NewContext();
                var ordered = await context.Rows.OrderBy(r => r.Amount).Select(r => r.Amount).ToListAsync();
                Assert.Equal(Ascending, ordered);
            }
        });
    }

    // Arithmetic, comparison and aggregation go through EF's ef_* helper functions, which take typed
    // decimal parameters and so were never culture-sensitive. Pinned so the fix is not credited with
    // more than it does, and so a regression in either half is attributable.
    [Fact]
    public async Task Comparison_arithmetic_and_aggregates_are_correct_under_a_comma_decimal_locale()
    {
        await SeedAsync();

        await WithCultureAsync("de-DE", async () =>
        {
            await using var context = NewContext();

            Assert.Equal(3, await context.Rows.CountAsync(r => r.Amount > 9.99m));
            Assert.Equal(3, await context.Rows.CountAsync(r => r.Amount * 2 > 19m));
            Assert.Equal(141.95m, await context.Rows.SumAsync(r => r.Amount));
            Assert.Equal(100.50m, await context.Rows.MaxAsync(r => r.Amount));
            Assert.Equal(2.00m, await context.Rows.MinAsync(r => r.Amount));
        });
    }

    // UPSTREAM PIN. The whole fix rests on EF Core emitting `COLLATE EF_DECIMAL` for a decimal ordering
    // and on that name being the one it registers. If EF renames it, changes the SQL, or fixes the
    // culture bug itself, this fails loudly and points at the reason rather than letting the override
    // silently stop applying.
    [Fact]
    public async Task Ef_core_still_orders_a_decimal_through_the_EF_DECIMAL_collation()
    {
        await using var context = NewContext();
        var sql = context.Rows.OrderBy(r => r.Amount).Select(r => r.Amount).ToQueryString();

        Assert.Contains($"COLLATE {SqliteCollations.Decimal}", sql, StringComparison.Ordinal);
        Assert.Equal("EF_DECIMAL", SqliteCollations.Decimal);
    }

    // The DDL must be untouched: no collation on the column, no migration, and every other tool still
    // reads the file. This is what makes the fix non-breaking.
    [Fact]
    public async Task The_fix_does_not_change_the_schema()
    {
        await SeedAsync();

        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'Rows'";
        var ddl = (string)(await command.ExecuteScalarAsync())!;

        Assert.Contains("\"Amount\" TEXT NOT NULL", ddl, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLATE", ddl, StringComparison.OrdinalIgnoreCase);
    }

    private ProbeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite($"Data Source={_dbPath}")
            .Options);

    private async Task SeedAsync()
    {
        await using var context = NewContext();
        await context.Database.EnsureCreatedAsync();
        if (await context.Rows.AnyAsync())
        {
            return;
        }

        context.Rows.AddRange(Unsorted.Select(a => new ProbeRow { Amount = a }));
        await context.SaveChangesAsync();
    }

    private List<string> ReadRawAmounts()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Amount FROM Rows ORDER BY Id";
        using var reader = command.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task WithCultureAsync(string culture, Func<Task> body)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(culture);
        try
        {
            await body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }
    }
}
