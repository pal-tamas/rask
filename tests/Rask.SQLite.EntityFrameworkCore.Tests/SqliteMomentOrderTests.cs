using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

public sealed class Rota
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public DateTimeOffset Start { get; set; }

    public DateTimeOffset? End { get; set; }

    public DateTimeOffset Logged { get; set; }
}

/// <summary>Moments, one of them stored as the app chose (UTC ticks) and one unique.</summary>
public sealed class RotaContext(DbContextOptions<RotaContext> options) : DbContext(options)
{
    public DbSet<Rota> Rotas => Set<Rota>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Rota>(b =>
        {
            b.HasIndex(s => s.Start).IsUnique();
            b.Property(s => s.Logged).HasConversion(new DateTimeOffsetToBinaryConverter());
        });
}

// UseRaskSqlite: a DateTimeOffset compares, orders and indexes by its instant, as SQL Server's datetimeoffset does.
public sealed class SqliteMomentOrderTests : IAsyncLifetime
{
    private readonly InMemorySqlite _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var context = Rotas();
        await context.Database.EnsureCreatedAsync(Cancellation);
        context.Rotas.AddRange(
            Starting("budapest", At(10, TimeSpan.FromHours(1))),
            Starting("london", At(8, TimeSpan.Zero)),
            Starting("new-york", At(5, TimeSpan.FromHours(-5))));
        await context.SaveChangesAsync(Cancellation);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public void Every_moment_column_is_created_with_the_moment_collation()
    {
        using var context = Rotas();

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Start\" TEXT COLLATE \"RASK_MOMENT\" NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"End\" TEXT COLLATE \"RASK_MOMENT\" NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"Code\" TEXT COLLATE \"RASK_TEXT\" NOT NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_moment_the_model_converted_itself_keeps_its_own_column()
    {
        using var context = Rotas();

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Logged\" INTEGER NOT NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_text_stored_is_the_text_EF_Core_stores_with_every_tick_and_the_offset()
    {
        await using var context = Rotas();
        context.Rotas.Add(Starting("precise", At(9, TimeSpan.FromHours(2)).AddTicks(1_234_567)));
        await context.SaveChangesAsync(Cancellation);

        var stored = await context.Database.SqlQueryRaw<string>("SELECT \"Start\" AS \"Value\" FROM \"Rotas\" WHERE \"Code\" = 'precise'")
            .SingleAsync(Cancellation);
        var read = await context.Rotas.Where(s => s.Code == "precise").Select(s => s.Start).SingleAsync(Cancellation);

        Assert.Equal("2026-03-04 09:00:00.1234567+02:00", stored);
        Assert.Equal(At(9, TimeSpan.FromHours(2)).AddTicks(1_234_567), read);
        Assert.Equal(TimeSpan.FromHours(2), read.Offset);
    }

    [Fact]
    public async Task Moments_order_by_their_instant_whatever_their_offset()
    {
        await using var context = Rotas();

        var ordered = await context.Rotas.OrderBy(s => s.Start).Select(s => s.Code).ToListAsync(Cancellation);
        var earliest = await context.Rotas.MinAsync(s => s.Start, Cancellation);

        // 08:00Z, 09:00Z (10:00+01:00), 10:00Z (05:00-05:00).
        Assert.Equal(["london", "budapest", "new-york"], ordered);
        Assert.Equal(TimeSpan.Zero, earliest.Offset);
    }

    [Fact]
    public async Task The_same_instant_in_another_offset_is_equal_and_a_range_compares_instants()
    {
        await using var context = Rotas();
        var nineUtc = At(9, TimeSpan.Zero);
        var eightUtcInTokyo = At(17, TimeSpan.FromHours(9));

        var equal = await context.Rotas.Where(s => s.Start == nineUtc).Select(s => s.Code).ToListAsync(Cancellation);
        var later = await context.Rotas.Where(s => s.Start > eightUtcInTokyo).OrderBy(s => s.Start).Select(s => s.Code)
            .ToListAsync(Cancellation);

        Assert.Equal(["budapest"], equal);
        Assert.Equal(["budapest", "new-york"], later);
    }

    [Fact]
    public async Task A_unique_index_refuses_the_same_instant_in_another_offset()
    {
        await using var context = Rotas();

        context.Rotas.Add(Starting("tokyo", At(18, TimeSpan.FromHours(9))));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Cancellation));
    }

    [Fact]
    public void A_plain_UseSqlite_keeps_EF_Core_s_own_column()
    {
        using var context = new RotaContext(new DbContextOptionsBuilder<RotaContext>().UseSqlite(_database.ConnectionString).Options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Start\" TEXT NOT NULL", script, StringComparison.Ordinal);
    }

    private RotaContext Rotas() =>
        new(new DbContextOptionsBuilder<RotaContext>().UseRaskSqliteAt(_database.ConnectionString).Options);

    private static DateTimeOffset At(int hour, TimeSpan offset) => new(2026, 3, 4, hour, 0, 0, offset);

    private static Rota Starting(string code, DateTimeOffset start) => new() { Code = code, Start = start, Logged = start };
}
