using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SqlServer.Tests;

public enum MeterKind
{
    Manual,
    Pump,
}

/// <summary>A reading whose columns the database computes.</summary>
public sealed class Meter
{
    public Guid Id { get; set; }

    public int Pistol { get; set; }

    public int? ModifiedPistol { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public string? Note { get; set; }

    public int CountedAt { get; private set; }

    public string Name { get; private set; } = "";

    public string Label { get; private set; } = "";

    public bool SamePistol { get; private set; }
}

/// <summary>A partner's grant: one live per partner.</summary>
public sealed class Permit
{
    public Guid Id { get; set; }

    public string? Partner { get; set; }

    public DateTime? RevokedAt { get; set; }

    public bool Active { get; set; }

    public MeterKind Kind { get; set; }
}

/// <summary>A tank the database defaults for rows written without its columns.</summary>
public sealed class Vessel
{
    public Guid Id { get; set; }

    public int Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTime CountedAt { get; set; }
}

/// <summary>The three provider-neutral declarations.</summary>
public sealed class NeutralContext(DbContextOptions<NeutralContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Meter>(b =>
        {
            b.Computed(m => m.CountedAt, m => m.ModifiedPistol ?? m.Pistol);
            b.Computed(m => m.Name, m => m.Year + "." + m.Month);
            b.Computed(m => m.Label, m => m.Note + "#" + m.ModifiedPistol);
            b.Computed(m => m.SamePistol, m => m.ModifiedPistol == m.Pistol);
        });
        modelBuilder.Entity<Permit>(b =>
        {
            b.HasIndex(p => p.Partner).IsUnique("A live grant already.").Where(p => p.RevokedAt == null);
            b.HasIndex(p => p.Kind).Where(p => p.Active && p.Kind == MeterKind.Pump);
        });
        modelBuilder.Entity<Vessel>(b =>
        {
            b.Default(v => v.Size, 1000);
            b.Default(v => v.CreatedAt, () => Clock.Now);
            b.Default(v => v.CountedAt, () => Clock.Now.UtcDateTime);
        });
    }
}

/// <summary>The declarations as SQL Server's own SQL, model and script only.</summary>
public sealed class NeutralModelTests
{
    [Fact]
    public void A_computed_column_is_a_persisted_one()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[CountedAt] AS COALESCE([ModifiedPistol], [Pistol]) PERSISTED", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_is_joined_with_plus_and_a_number_cast_as_C_sharp_writes_it()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[Name] AS (CAST([Year] AS nvarchar(20)) + N'.' + CAST([Month] AS nvarchar(20))) PERSISTED", script, StringComparison.Ordinal);
        Assert.Contains(
            "[Label] AS (COALESCE([Note], N'') + N'#' + COALESCE(CAST([ModifiedPistol] AS nvarchar(20)), N'')) PERSISTED", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comparison_in_a_value_s_place_is_a_bit_true_for_two_nulls_as_in_C_sharp()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains(
            "[SamePistol] AS CASE WHEN ([ModifiedPistol] IS NOT NULL AND [ModifiedPistol] = [Pistol]) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END PERSISTED",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_filtered_unique_index_also_leaves_out_the_nulls_of_its_nullable_key()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("ON [Permit] ([Partner]) WHERE [Partner] IS NOT NULL AND [RevokedAt] IS NULL;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filter_writes_a_bit_bare_and_an_enum_as_its_number()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("ON [Permit] ([Kind]) WHERE [Active] = 1 AND [Kind] = 1;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_clock_default_is_the_server_s_UTC_time()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[Size] int NOT NULL DEFAULT 1000", script, StringComparison.Ordinal);
        Assert.Contains("[CreatedAt] datetimeoffset NOT NULL DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), 0))", script, StringComparison.Ordinal);
        Assert.Contains("[CountedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME())", script, StringComparison.Ordinal);
    }

    private static DbContextOptions<NeutralContext> Options() =>
        new DbContextOptionsBuilder<NeutralContext>().UseSqlServer("Server=unused;Database=unused").Options;
}
