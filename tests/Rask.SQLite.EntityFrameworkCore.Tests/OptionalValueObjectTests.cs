using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>
///     An optional value object whose members are all optional — <c>Opening? Start</c> over
///     <c>Opening(DateTime? Date, decimal? Quantity)</c> — is held in its row's own columns through <c>UseRaskSqlite</c>:
///     every column empty is none. The SQL Server and PostgreSQL halves are in Rask.Providers.E2E.Tests'
///     <c>OptionalValueObjectParityTests</c>.
/// </summary>
public sealed class OptionalValueObjectTests : IAsyncLifetime
{
    private static readonly DateTime Day = new(2026, 3, 4, 0, 0, 0, DateTimeKind.Unspecified);

    private readonly string _connectionString = $"Data Source=file:rask-optional-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private SqliteConnection? _keeper;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _keeper = new SqliteConnection(_connectionString);
        await _keeper.OpenAsync(Ct);
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_keeper is not null)
        {
            await _keeper.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_optional_value_with_every_member_optional_reads_back_as_none_or_as_what_it_holds()
    {
        await using (var db = NewContext())
        {
            db.AddRange(
                new Part { Id = 1 },
                new Part { Id = 2, Start = new Opening(Day, 5m) },
                new Part { Id = 3, Start = new Opening(Day, null) },
                new Part { Id = 4, Start = new Opening(null, 5m) });
            await db.SaveChangesAsync(Ct);
        }

        await using var read = NewContext();
        var starts = await read.Set<Part>().OrderBy(p => p.Id).Select(p => p.Start).ToListAsync(Ct);

        Assert.Equal([null, new Opening(Day, 5m), new Opening(Day, null), new Opening(null, 5m)], starts);
    }

    [Fact]
    public async Task A_query_for_none_asks_that_every_column_is_empty()
    {
        await using var db = NewContext();
        db.AddRange(new Part { Id = 1 }, new Part { Id = 2, Start = new Opening(null, 5m) }, new Part { Id = 3, Start = new Opening(Day, null) });
        await db.SaveChangesAsync(Ct);

        var none = await db.Set<Part>().Where(p => p.Start == null).Select(p => p.Id).ToListAsync(Ct);
        var some = await db.Set<Part>().Where(p => p.Start != null).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(Ct);

        Assert.Equal([1], none);
        Assert.Equal([2, 3], some);
    }

    [Fact]
    public async Task Taking_the_value_away_empties_every_column_and_giving_it_back_fills_them()
    {
        await using (var db = NewContext())
        {
            db.AddRange(new Part { Id = 1, Start = new Opening(Day, 5m) }, new Part { Id = 2 });
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NewContext())
        {
            var parts = await db.Set<Part>().OrderBy(p => p.Id).ToListAsync(Ct);
            parts[0].Start = null;
            parts[1].Start = new Opening(Day, null);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = NewContext();
        var starts = await read.Set<Part>().OrderBy(p => p.Id).Select(p => p.Start).ToListAsync(Ct);
        var empty = await read.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Parts WHERE Start_Date IS NULL AND Start_Quantity IS NULL").SingleAsync(Ct);

        Assert.Equal([null, new Opening(Day, null)], starts);
        Assert.Equal(1, empty);
    }

    [Fact]
    public async Task A_value_whose_members_are_all_empty_is_stored_as_none()
    {
        await using (var db = NewContext())
        {
            db.Add(new Part { Id = 1, Start = new Opening(null, null) });
            await db.SaveChangesAsync(Ct);
        }

        await using var read = NewContext();
        var start = await read.Set<Part>().Select(p => p.Start).SingleAsync(Ct);

        Assert.Null(start);
    }

    [Fact]
    public void A_plain_context_on_a_plain_UseSqlite_refuses_the_value_as_EF_Core_does()
    {
        using var db = new OpeningContext(new DbContextOptionsBuilder<OpeningContext>().UseSqlite("Data Source=:memory:").Options);

        var refused = Assert.Throws<InvalidOperationException>(() => db.Model);

        Assert.Contains("only optional properties", refused.Message, StringComparison.Ordinal);
    }

    private OpeningContext NewContext() =>
        new(new DbContextOptionsBuilder<OpeningContext>().UseRaskSqliteAt(_connectionString).Options);

    public sealed record Opening(DateTime? Date, decimal? Quantity);

    public sealed class Part
    {
        public int Id { get; set; }

        public Opening? Start { get; set; }
    }

    private sealed class OpeningContext(DbContextOptions<OpeningContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Part>(b =>
            {
                b.ToTable("Parts");
                b.ComplexProperty(p => p.Start);
            });
    }
}
