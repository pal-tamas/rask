using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A tally fueling's id: a Guid the application makes, kept as text where the column is text (SQLite's every Guid).</summary>
public readonly partial record struct TallyFuelingId(Guid Value);

/// <summary>The hour a tally fueling was read at: a one-value value object over a number, which has an order.</summary>
public sealed partial record TallyHour(int Value) : Value;

/// <summary>A fueling numbered at its pistol by its hour — a value object — then its typed id: TA's <c>Fueling</c> shape.</summary>
public sealed class TallyFueling : Aggregate<TallyFuelingId>
{
    private TallyFueling() { } // EF materialization

    public string Pistol { get; private set; } = "";

    public TallyHour Hour { get; private set; } = null!;

    public int? Sequence { get; private set; }

    public static TallyFueling At(string pistol, int hour, int id) =>
        new() { Id = new TallyFuelingId(new Guid($"00000000-0000-7000-8000-{id:D12}")), Pistol = pistol, Hour = TallyHour.Create(hour) };

    public static void Configure(EntityTypeBuilder<TallyFueling> builder) =>
        builder.Numbered(f => f.Sequence, within: f => f.Pistol, by: f => new { f.Hour, f.Id });
}

/// <summary>The same fueling with none of its members typed: the order the typed one must be sent as.</summary>
public sealed class TallyPlainFueling : Aggregate<Guid>
{
    private TallyPlainFueling() { } // EF materialization

    public string Pistol { get; private set; } = "";

    public int Hour { get; private set; }

    public int? Sequence { get; private set; }

    public static void Configure(EntityTypeBuilder<TallyPlainFueling> builder) =>
        builder.Numbered(f => f.Sequence, within: f => f.Pistol, by: f => new { f.Hour, f.Id });
}

/// <summary>
///     <c>b.Numbered(…)</c> ordered by a typed id and a value object: each is ordered by as it is mapped, so the database
///     orders by its column — a typed id kept as text included, whose Guid EF Core cannot translate.
/// </summary>
public sealed class NumberedTypedIdTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task Rows_ordered_by_a_typed_id_kept_as_text_are_numbered_by_that_column()
    {
        await using var database = await TestDatabase.Start(
            o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: TestContext.Current.CancellationToken);
        TallyFueling[] fuelings = [TallyFueling.At("a", 8, id: 3), TallyFueling.At("a", 8, id: 1), TallyFueling.At("a", 8, id: 2)];

        database.Context.AddRange(fuelings);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([3, 1, 2], await SequencesAsync(fuelings));
    }

    [Fact]
    public async Task An_earlier_hour_saved_later_moves_the_rows_after_it_on_by_the_value_objects_value()
    {
        await using var database = await TestDatabase.Start(
            o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: TestContext.Current.CancellationToken);
        TallyFueling[] fuelings = [TallyFueling.At("a", 9, id: 1), TallyFueling.At("a", 10, id: 2)];
        database.Context.AddRange(fuelings);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var earlier = TallyFueling.At("a", 8, id: 3);

        database.Context.Add(earlier);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2, 3, 1], await SequencesAsync([.. fuelings, earlier]));
    }

    [Fact]
    public async Task A_typed_id_kept_as_text_is_ordered_by_its_column_as_the_untyped_id_is()
    {
        await using var database = await TestDatabase.Start(
            o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: TestContext.Current.CancellationToken);

        var typed = OrderOf<TallyFueling>(database.Context, f => new { f.Hour, f.Id });
        var plain = OrderOf<TallyPlainFueling>(database.Context, f => new { f.Hour, f.Id });

        Assert.Equal("ORDER BY \"t\".\"hour\", \"t\".\"id\"", typed);
        Assert.Equal(plain, typed);
    }

    [Fact]
    public void A_typed_id_in_a_native_Guid_column_through_the_apps_converter_is_ordered_by_its_column_as_the_untyped_id_is()
    {
        // Offline, in the options shape ForUpdateSqlServerReadTests already built: no new EF Core service provider.
        using var context = new GaugeContext(OptionsLayout.LikeAddDbContext<GaugeContext>()
            .UseSqlServer("Server=localhost;Database=rask;User Id=sa;Password=x;TrustServerCertificate=true")
            .Options);

        var typed = OrderOf<GaugedFueling>(context, f => new { f.Hour, f.Id });
        var plain = OrderOf<GaugedPlainFueling>(context, f => new { f.Hour, f.Id });

        Assert.Equal("ORDER BY [g].[Hour], [g].[Id]", typed);
        Assert.Equal(plain, typed);
    }

    public sealed class GaugedFueling
    {
        public TallyFuelingId Id { get; set; }

        public int Hour { get; set; }
    }

    public sealed class GaugedPlainFueling
    {
        public Guid Id { get; set; }

        public int Hour { get; set; }
    }

    // The typed id stored in a uniqueidentifier column through a converter of the app's own, not Rask's.
    private sealed class GaugeContext(DbContextOptions<GaugeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GaugedFueling>().Property(f => f.Id).HasConversion(id => id.Value, value => new TallyFuelingId(value));
            modelBuilder.Entity<GaugedPlainFueling>();
        }
    }

    // The ORDER BY a numbering sorts its rows with, as the provider is sent it.
    private static string OrderOf<TEntity>(DbContext context, Expression<Func<TEntity, object>> by)
        where TEntity : class
    {
        var row = Expression.Parameter(typeof(TEntity), "row");
        var sql = NumberingOrder.Sorted(context.Set<TEntity>(), row, NumberingOrder.Members(by), descending: false).ToQueryString();
        return sql[sql.IndexOf("ORDER BY", StringComparison.Ordinal)..].Trim();
    }

    private static async Task<int?[]> SequencesAsync(IEnumerable<TallyFueling> fuelings)
    {
        await using var db = Db.CreateOwnContext();
        var stored = await db.Set<TallyFueling>().ToDictionaryAsync(f => f.Id, f => f.Sequence, TestContext.Current.CancellationToken);
        return [.. fuelings.Select(f => stored[f.Id])];
    }
}
