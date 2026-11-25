using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>
///     A fueling numbered at its pistol by moment, then pump number, then id: a merged one is not counted, nor one a
///     share was distributed from, and an IBC part takes its fill's number.
/// </summary>
public sealed class PumpFueling : Aggregate<Guid>
{
    private PumpFueling() { } // EF materialization

    public string Pistol { get; private set; } = "";

    public DateTime Moment { get; private set; }

    public int Pump { get; private set; }

    public bool Merged { get; private set; }

    public Guid? FillId { get; private set; }

    public int? Sequence { get; private set; }

    public static PumpFueling At(string pistol, int hour, int pump = 1) =>
        new() { Id = Guid.CreateVersion7(), Pistol = pistol, Moment = new DateTime(2026, 11, 24, hour, 0, 0, DateTimeKind.Utc), Pump = pump };

    public static PumpFueling PartOf(PumpFueling fill, int hour) =>
        new() { Id = Guid.CreateVersion7(), Pistol = fill.Pistol, Moment = new DateTime(2026, 11, 24, hour, 0, 0, DateTimeKind.Utc), FillId = fill.Id };

    public void Merge() => Merged = true;

    public void MoveTo(string pistol) => Pistol = pistol;

    public void Reschedule(int hour) => Moment = new DateTime(2026, 11, 24, hour, 0, 0, DateTimeKind.Utc);

    public static void Configure(EntityTypeBuilder<PumpFueling> builder) =>
        builder.Numbered(f => f.Sequence, within: f => f.Pistol, by: f => new { f.Moment, f.Pump, f.Id }, skip: f => f.Merged)
            .Same(f => f.FillId)
            .SkipWhen<PumpShare, Guid>((f, shares) => shares.Any(s => s.FromId == f.Id), s => s.FromId);
}

/// <summary>A share distributed from a fueling, which leaves that fueling out of its pistol's count.</summary>
public sealed class PumpShare : Aggregate<Guid>
{
    private PumpShare() { } // EF materialization

    public Guid FromId { get; private set; }

    public static PumpShare From(PumpFueling fueling) => new() { Id = Guid.CreateVersion7(), FromId = fueling.Id };
}

/// <summary>
///     A fueling counted at the pistol it was corrected to, else the one it was read at: a computed column, the group of
///     its numbering — as data-writes.md declares it.
/// </summary>
public sealed class CorrectedFueling : Aggregate<Guid>
{
    private CorrectedFueling() { } // EF materialization

    public string Pistol { get; private set; } = "";

    public string? ModifiedPistol { get; private set; }

    public string? CountedAt { get; private set; }

    public DateTime Moment { get; private set; }

    public int? Sequence { get; private set; }

    public static CorrectedFueling At(string pistol, int hour) =>
        new() { Id = Guid.CreateVersion7(), Pistol = pistol, Moment = new DateTime(2026, 11, 24, hour, 0, 0, DateTimeKind.Utc) };

    public void CorrectTo(string pistol) => ModifiedPistol = pistol;

    public static void Configure(EntityTypeBuilder<CorrectedFueling> builder)
    {
        builder.Computed(f => f.CountedAt, f => f.ModifiedPistol ?? f.Pistol);
        builder.Numbered(f => f.Sequence, within: f => f.CountedAt, by: f => new { f.Moment, f.Id });
    }
}

/// <summary>A reading numbered within a lane the database computes, which C# cannot compute for a reading of no width.</summary>
public sealed class LaneReading : Aggregate<Guid>
{
    private LaneReading() { } // EF materialization

    public int? Offset { get; private set; }

    public int? Width { get; private set; }

    public int? Lane { get; private set; }

    public int Hour { get; private set; }

    public int? Sequence { get; private set; }

    public static LaneReading At(int offset, int width, int hour) => new() { Id = Guid.CreateVersion7(), Offset = offset, Width = width, Hour = hour };

    public static void Configure(EntityTypeBuilder<LaneReading> builder)
    {
        builder.Computed(r => r.Lane, r => r.Offset / r.Width);
        builder.Numbered(r => r.Sequence, within: r => r.Lane, by: r => new { r.Hour, r.Id });
    }
}

/// <summary><c>b.Numbered(…)</c>: the running number Rask keeps in every save, with no SQL in the app.</summary>
public sealed class NumberedTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task Rows_are_numbered_in_order_within_each_group()
    {
        await using var database = await StartDatabaseAsync();
        PumpFueling[] fuelings = [PumpFueling.At("a", 9), PumpFueling.At("a", 8), PumpFueling.At("a", 8, pump: 2), PumpFueling.At("b", 10)];

        await SaveAsync(database, fuelings);

        Assert.Equal([3, 1, 2, 1], await SequencesAsync(fuelings));
    }

    [Fact]
    public async Task An_earlier_row_saved_later_moves_the_ones_after_it_on()
    {
        await using var database = await StartDatabaseAsync();
        PumpFueling[] fuelings = [PumpFueling.At("a", 8), PumpFueling.At("a", 10)];
        await SaveAsync(database, fuelings);
        var earlier = PumpFueling.At("a", 9);

        await SaveAsync(database, earlier);

        Assert.Equal([1, 3, 2], await SequencesAsync([.. fuelings, earlier]));
    }

    [Fact]
    public async Task A_skipped_row_has_none_and_the_ones_after_it_close_up_and_the_tracked_row_is_told()
    {
        await using var database = await StartDatabaseAsync();
        PumpFueling[] fuelings = [PumpFueling.At("a", 8), PumpFueling.At("a", 9), PumpFueling.At("a", 10)];
        await SaveAsync(database, fuelings);

        fuelings[1].Merge();
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, null, 2], await SequencesAsync(fuelings));
        Assert.Equal(2, fuelings[2].Sequence);
        Assert.Equal(EntityState.Unchanged, database.Context.Entry(fuelings[2]).State);
    }

    [Fact]
    public async Task A_row_moved_to_another_group_takes_its_place_there_and_leaves_none_behind()
    {
        await using var database = await StartDatabaseAsync();
        PumpFueling[] fuelings = [PumpFueling.At("a", 8), PumpFueling.At("a", 9), PumpFueling.At("b", 10)];
        await SaveAsync(database, fuelings);

        fuelings[0].MoveTo("b");
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 1, 2], await SequencesAsync(fuelings));
    }

    [Fact]
    public async Task A_row_moved_earlier_in_its_group_takes_its_new_place_and_another_group_is_untouched()
    {
        await using var database = await StartDatabaseAsync();
        PumpFueling[] fuelings = [PumpFueling.At("a", 8), PumpFueling.At("a", 10), PumpFueling.At("a", 12), PumpFueling.At("b", 11)];
        await SaveAsync(database, fuelings);

        fuelings[2].Reschedule(9);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 3, 2, 1], await SequencesAsync(fuelings));
    }

    [Fact]
    public async Task A_deleted_row_s_followers_close_up()
    {
        await using var database = await StartDatabaseAsync();
        PumpFueling[] fuelings = [PumpFueling.At("a", 8), PumpFueling.At("a", 9), PumpFueling.At("a", 10)];
        await SaveAsync(database, fuelings);

        database.Context.Remove(fuelings[0]);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], await SequencesAsync(fuelings[1..]));
    }

    [Fact]
    public async Task A_part_takes_its_fill_s_number_and_follows_it()
    {
        await using var database = await StartDatabaseAsync();
        var fill = PumpFueling.At("a", 9);
        await SaveAsync(database, PumpFueling.At("a", 8), fill);
        var part = PumpFueling.PartOf(fill, hour: 11);
        await SaveAsync(database, part);
        var earliest = PumpFueling.At("a", 7);

        await SaveAsync(database, earliest);

        Assert.Equal([3, 3, 1], await SequencesAsync([fill, part, earliest]));
    }

    [Fact]
    public async Task A_row_another_table_leaves_out_has_none_until_that_row_goes()
    {
        await using var database = await StartDatabaseAsync();
        PumpFueling[] fuelings = [PumpFueling.At("a", 8), PumpFueling.At("a", 9), PumpFueling.At("a", 10)];
        await SaveAsync(database, fuelings);
        var share = PumpShare.From(fuelings[0]);

        database.Context.Add(share);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var distributed = await SequencesAsync(fuelings);
        database.Context.Remove(share);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([null, 1, 2], distributed);
        Assert.Equal([1, 2, 3], await SequencesAsync(fuelings));
    }

    [Fact]
    public async Task A_save_inside_a_transaction_that_rolls_back_leaves_the_numbers_as_they_were()
    {
        await using var database = await StartDatabaseAsync();
        PumpFueling[] fuelings = [PumpFueling.At("a", 8), PumpFueling.At("a", 10)];
        await SaveAsync(database, fuelings);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await using var db = Db.CreateOwnContext();
            db.Add(PumpFueling.At("a", 7));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("rolled back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([1, 2], await SequencesAsync(fuelings));
    }

    [Fact]
    public async Task A_row_grouped_by_a_computed_column_is_numbered_where_the_database_computes_it()
    {
        await using var database = await StartDatabaseAsync();
        CorrectedFueling[] fuelings = [CorrectedFueling.At("a", 8), CorrectedFueling.At("a", 10)];
        await SaveCorrectedAsync(database, fuelings);
        var between = CorrectedFueling.At("a", 9);

        await SaveCorrectedAsync(database, between);

        Assert.Equal([1, 3, 2], await CorrectedSequencesAsync([.. fuelings, between]));
    }

    [Fact]
    public async Task A_row_corrected_to_another_pistol_numbers_both_pistols_anew()
    {
        await using var database = await StartDatabaseAsync();
        CorrectedFueling[] fuelings = [CorrectedFueling.At("a", 8), CorrectedFueling.At("a", 9), CorrectedFueling.At("a", 10), CorrectedFueling.At("b", 11)];
        await SaveCorrectedAsync(database, fuelings);

        fuelings[1].CorrectTo("b");
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 1, 2, 2], await CorrectedSequencesAsync(fuelings));
        Assert.Equal(2, fuelings[2].Sequence);
    }

    [Fact]
    public async Task A_computed_group_C_sharp_cannot_compute_is_read_back_from_the_database_before_numbering()
    {
        await using var database = await StartDatabaseAsync();
        LaneReading[] readings = [LaneReading.At(4, 2, hour: 9), LaneReading.At(5, 2, hour: 8), LaneReading.At(4, 0, hour: 7)];

        database.Context.AddRange(readings);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var db = Db.CreateOwnContext();
        var stored = await db.Set<LaneReading>().ToDictionaryAsync(r => r.Id, r => r.Sequence, TestContext.Current.CancellationToken);
        Assert.Equal([2, 1, null], readings.Select(r => stored[r.Id]));
    }

    private static async Task SaveAsync(TestDatabase database, params PumpFueling[] fuelings)
    {
        database.Context.AddRange(fuelings);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<int?[]> SequencesAsync(IEnumerable<PumpFueling> fuelings)
    {
        await using var db = Db.CreateOwnContext();
        var stored = await db.Set<PumpFueling>().ToDictionaryAsync(f => f.Id, f => f.Sequence, TestContext.Current.CancellationToken);
        return [.. fuelings.Select(f => stored[f.Id])];
    }

    private static async Task SaveCorrectedAsync(TestDatabase database, params CorrectedFueling[] fuelings)
    {
        database.Context.AddRange(fuelings);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<int?[]> CorrectedSequencesAsync(IEnumerable<CorrectedFueling> fuelings)
    {
        await using var db = Db.CreateOwnContext();
        var stored = await db.Set<CorrectedFueling>().ToDictionaryAsync(f => f.Id, f => f.Sequence, TestContext.Current.CancellationToken);
        return [.. fuelings.Select(f => stored[f.Id])];
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: TestContext.Current.CancellationToken);
}
