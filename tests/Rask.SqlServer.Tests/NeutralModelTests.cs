using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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

    public bool Sealed { get; set; }

    public DateTime SeenAt { get; set; }
}

/// <summary>The three provider-neutral declarations.</summary>
public sealed class NeutralContext(DbContextOptions<NeutralContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        Declare(modelBuilder);
    }

    internal static void Declare(ModelBuilder modelBuilder)
    {
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
            b.Default(v => v.Sealed, false);
            b.Default(v => v.SeenAt, () => Clock.Wall);
        });
    }
}

/// <summary>The same declarations, under an app's own constraint names — TA's.</summary>
public sealed class NamedNeutralContext(DbContextOptions<NamedNeutralContext> options) : RaskDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.ConstraintNames(n => n.Kind == ConstraintKind.Default ? $"{n.Table}_{n.Columns[0]}_df" : null);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        NeutralContext.Declare(modelBuilder);
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

        Assert.Contains("[counted_at] AS COALESCE([modified_pistol], [pistol]) PERSISTED", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_is_joined_with_plus_and_a_number_cast_as_C_sharp_writes_it()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[name] AS (CAST([year] AS nvarchar(20)) + N'.' + CAST([month] AS nvarchar(20))) PERSISTED", script, StringComparison.Ordinal);
        Assert.Contains(
            "[label] AS (COALESCE([note], N'') + N'#' + COALESCE(CAST([modified_pistol] AS nvarchar(20)), N'')) PERSISTED", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comparison_in_a_value_s_place_is_a_bit_true_for_two_nulls_as_in_C_sharp()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains(
            "[same_pistol] AS CASE WHEN ([modified_pistol] IS NOT NULL AND [modified_pistol] = [pistol]) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END PERSISTED",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_filtered_unique_index_also_leaves_out_the_nulls_of_its_nullable_key()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("ON [permits] ([partner]) WHERE [partner] IS NOT NULL AND [revoked_at] IS NULL;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filter_writes_a_bit_bare_and_an_enum_as_its_number()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("ON [permits] ([kind]) WHERE [active] = 1 AND [kind] = 1;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_clock_default_is_the_server_s_UTC_time()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[size] int NOT NULL CONSTRAINT [df_vessels_size] DEFAULT (1000)", script, StringComparison.Ordinal);
        Assert.Contains("[created_at] datetimeoffset NOT NULL CONSTRAINT [df_vessels_created_at] DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), 0))", script, StringComparison.Ordinal);
        Assert.Contains("[counted_at] datetime2 NOT NULL CONSTRAINT [df_vessels_counted_at] DEFAULT (SYSUTCDATETIME())", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loose_search_is_the_case_and_accent_insensitive_Latin1_collation_in_the_query_alone()
    {
        using var context = new NeutralContext(Options());
        var typed = "kovacs";

        var sql = context.Set<Meter>().Where(m => m.Name.Loosely().Contains(typed)).ToQueryString();

        Assert.Contains("[m].[name] COLLATE Latin1_General_100_CI_AI", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Latin1_General_100_CI_AI", context.Database.GenerateCreateScript(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_bit_default_is_written_bare_and_the_wall_clock_as_CURRENT_TIMESTAMP_as_a_legacy_schema_has_them()
    {
        using var context = new NeutralContext(Options());

        var vessel = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Vessel))!;

        Assert.Equal("0", vessel.FindProperty(nameof(Vessel.Sealed))!.GetDefaultValueSql());
        Assert.Equal("CURRENT_TIMESTAMP", vessel.FindProperty(nameof(Vessel.SeenAt))!.GetDefaultValueSql());
        Assert.Equal("1000", vessel.FindProperty(nameof(Vessel.Size))!.GetDefaultValueSql());
    }

    [Fact]
    public void An_app_s_constraint_names_name_the_defaults_too_the_clock_s_among_them()
    {
        using var context = new NamedNeutralContext(
            new DbContextOptionsBuilder<NamedNeutralContext>().UseSqlServer("Server=unused;Database=unused").Options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[size] int NOT NULL CONSTRAINT [vessels_size_df] DEFAULT (1000)", script, StringComparison.Ordinal);
        Assert.Contains(
            "[created_at] datetimeoffset NOT NULL CONSTRAINT [vessels_created_at_df] DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), 0))",
            script,
            StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT [vessels_counted_at_df] DEFAULT (SYSUTCDATETIME())", script, StringComparison.Ordinal);
    }

    private static DbContextOptions<NeutralContext> Options() =>
        new DbContextOptionsBuilder<NeutralContext>().UseSqlServer("Server=unused;Database=unused").Options;
}
