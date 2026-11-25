using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Providers.E2E.Tests;

/// <summary>A fueling numbered at its pistol by moment, pump number and id; one a share was distributed from is not counted.</summary>
public sealed class NumberedFueling : Aggregate<string>
{
    private NumberedFueling() { } // EF materialization

    public string Pistol { get; private set; } = "";

    public DateTime Moment { get; private set; }

    public int Pump { get; private set; }

    public bool Merged { get; private set; }

    public string? FillId { get; private set; }

    public int? Sequence { get; private set; }

    public static NumberedFueling At(string pistol, int minute, int pump = 1, string? fill = null) =>
        new()
        {
            Id = Guid.CreateVersion7().ToString(), Pistol = pistol, Pump = pump, FillId = fill,
            Moment = new DateTime(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc).AddMinutes(minute),
        };

    public void Merge() => Merged = true;

    internal static void Number(EntityTypeBuilder<NumberedFueling> builder)
    {
        builder.Property(f => f.Id).HasMaxLength(36);
        builder.Property(f => f.Pistol).HasMaxLength(20);
        builder.Property(f => f.FillId).HasMaxLength(36);
        builder.HasIndex(f => new { f.Pistol, f.Moment });
        builder.Numbered(f => f.Sequence, within: f => f.Pistol, by: f => new { f.Moment, f.Pump, f.Id }, skip: f => f.Merged)
            .Same(f => f.FillId)
            .SkipWhen<NumberedShare, string>((f, shares) => shares.Any(s => s.FromId == f.Id), s => s.FromId);
    }
}

/// <summary>A fueling counted at the pistol it was corrected to, else the one it was read at: a computed group.</summary>
public sealed class CorrectedNumberedFueling : Aggregate<string>
{
    private CorrectedNumberedFueling() { } // EF materialization

    public string Pistol { get; private set; } = "";

    public string? ModifiedPistol { get; private set; }

    public string? CountedAt { get; private set; }

    public DateTime Moment { get; private set; }

    public int? Sequence { get; private set; }

    public static CorrectedNumberedFueling At(string pistol, int minute) =>
        new() { Id = Guid.CreateVersion7().ToString(), Pistol = pistol, Moment = new DateTime(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc).AddMinutes(minute) };

    public void CorrectTo(string pistol) => ModifiedPistol = pistol;

    internal static void Number(EntityTypeBuilder<CorrectedNumberedFueling> builder)
    {
        builder.Property(f => f.Id).HasMaxLength(36);
        builder.Property(f => f.Pistol).HasMaxLength(20);
        builder.Property(f => f.ModifiedPistol).HasMaxLength(20);
        builder.Property(f => f.CountedAt).HasMaxLength(20);
        builder.Computed(f => f.CountedAt, f => f.ModifiedPistol ?? f.Pistol);
        builder.Numbered(f => f.Sequence, within: f => f.CountedAt, by: f => new { f.Moment, f.Id });
    }
}

public sealed class NumberedShare : Aggregate<Guid>
{
    private NumberedShare() { } // EF materialization

    public string FromId { get; private set; } = "";

    public static NumberedShare From(NumberedFueling fueling) => new() { Id = Guid.CreateVersion7(), FromId = fueling.Id };
}

public sealed class NumberedDbContext(DbContextOptions<NumberedDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_numbered";

    // Computed(…) is written as the provider's SQL by Rask's conventions.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<NumberedFueling>(NumberedFueling.Number);
        modelBuilder.Entity<CorrectedNumberedFueling>(CorrectedNumberedFueling.Number);
        modelBuilder.Entity<NumberedShare>().Property(s => s.FromId).HasMaxLength(36);
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
///     <c>b.Numbered(…)</c> on every provider: the same numbers from the same saves — an earlier row, a skipped one, a
///     part, another table's row — and saves racing into one group still number it 1, 2, 3 … with no gap or twin.
/// </summary>
public abstract class NumberedParityTests(ProviderLeg leg) : ParityTests<NumberedDbContext>(leg)
{
    [Fact]
    public async Task Saves_number_each_group_in_order_and_keep_it_so()
    {
        RequireServer();
        var fill = NumberedFueling.At("a", 20);
        NumberedFueling[] first = [NumberedFueling.At("a", 30), fill, NumberedFueling.At("a", 10, pump: 2), NumberedFueling.At("b", 5)];
        await SaveAsync(first);
        var part = NumberedFueling.At("a", 40, fill: fill.Id);
        var earlier = NumberedFueling.At("a", 10, pump: 1);

        await SaveAsync(part, earlier);

        Assert.Equal([4, 3, 2, 1, 3, 1], await SequencesAsync([.. first, part, earlier]));
    }

    [Fact]
    public async Task A_row_left_out_by_its_own_value_or_another_table_s_row_has_none()
    {
        RequireServer();
        NumberedFueling[] fuelings = [NumberedFueling.At("a", 10), NumberedFueling.At("a", 20), NumberedFueling.At("a", 30)];
        await SaveAsync(fuelings);
        await using var db = Open();
        var tracked = await db.Set<NumberedFueling>().SingleAsync(f => f.Id == fuelings[2].Id, Ct);

        tracked.Merge();
        db.Add(NumberedShare.From(fuelings[0]));
        await db.SaveChangesAsync(Ct);

        Assert.Equal([null, 1, null], await SequencesAsync(fuelings));
    }

    [Fact]
    public async Task Saves_racing_into_one_group_leave_it_numbered_without_a_gap_or_a_twin()
    {
        RequireServer();
        await SaveAsync(NumberedFueling.At("a", 0));

        await Task.WhenAll(Enumerable.Range(1, 8).Select(i => SaveAsync(NumberedFueling.At("a", 100 - i))));

        await using var db = Open();
        var numbers = await db.Set<NumberedFueling>().OrderBy(f => f.Moment).Select(f => f.Sequence).ToListAsync(Ct);
        Assert.Equal(Enumerable.Range(1, 9).Select(i => (int?)i), numbers);
    }

    [Fact]
    public async Task A_computed_group_numbers_a_row_where_the_database_puts_it_and_a_correction_numbers_both_groups()
    {
        RequireServer();
        CorrectedNumberedFueling[] fuelings = [CorrectedNumberedFueling.At("a", 10), CorrectedNumberedFueling.At("a", 30), CorrectedNumberedFueling.At("b", 25)];
        await SaveAsync(fuelings);
        var between = CorrectedNumberedFueling.At("a", 20);
        await SaveAsync(between);
        var inserted = await CorrectedSequencesAsync([.. fuelings, between]);
        await using var db = Open();
        var tracked = await db.Set<CorrectedNumberedFueling>().SingleAsync(f => f.Id == between.Id, Ct);

        tracked.CorrectTo("b");
        await db.SaveChangesAsync(Ct);

        Assert.Equal([1, 3, 1, 2], inserted);
        Assert.Equal([1, 2, 2, 1], await CorrectedSequencesAsync([.. fuelings, between]));
    }

    private protected override NumberedDbContext Open() => Leg.Open<NumberedDbContext>(o => new NumberedDbContext(o));

    private async Task SaveAsync(params CorrectedNumberedFueling[] fuelings)
    {
        await using var db = Open();
        db.AddRange(fuelings);
        await db.SaveChangesAsync(Ct);
    }

    private async Task SaveAsync(params NumberedFueling[] fuelings)
    {
        await using var db = Open();
        db.AddRange(fuelings);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<int?[]> CorrectedSequencesAsync(IEnumerable<CorrectedNumberedFueling> fuelings)
    {
        await using var db = Open();
        var stored = await db.Set<CorrectedNumberedFueling>().ToDictionaryAsync(f => f.Id, f => f.Sequence, Ct);
        return [.. fuelings.Select(f => stored[f.Id])];
    }

    private async Task<int?[]> SequencesAsync(IEnumerable<NumberedFueling> fuelings)
    {
        await using var db = Open();
        var stored = await db.Set<NumberedFueling>().ToDictionaryAsync(f => f.Id, f => f.Sequence, Ct);
        return [.. fuelings.Select(f => stored[f.Id])];
    }
}

public sealed class SqliteNumberedParityTests() : NumberedParityTests(ProviderLeg.Sqlite("numbered"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerNumberedParityTests() : NumberedParityTests(ProviderLeg.SqlServer("rask_e2e_numbered"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresNumberedParityTests() : NumberedParityTests(ProviderLeg.Postgres(NumberedDbContext.Schema));
