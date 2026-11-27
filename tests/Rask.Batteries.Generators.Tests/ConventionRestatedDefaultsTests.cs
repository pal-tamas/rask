using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     RASK192 beyond names: an <c>OnDelete</c>, a facet, an <c>IsRequired()</c>, a key, its generation, the version's
///     token or a conversion that says what Rask or EF Core does anyway — and silence wherever the line says more.
/// </summary>
public sealed class ConventionRestatedDefaultsTests
{
    private static Task<IReadOnlyList<Diagnostic>> Analyzed(string tank, string pump = "", string elsewhere = "") =>
        ModelStateAnalyzerHarness.RunAsync(new ConventionRestatedAnalyzer(), $$"""
            using System;
            using System.Collections.Generic;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            using Rask.Values;
            namespace Fuel;
            public readonly partial record struct TankId(Guid Value);
            public readonly partial record struct PumpId(int Value);
            public sealed record DriverName(string Value) : Text(Value, MaxLength: 100);
            public sealed record Litres(decimal Value) : Number(Value, Scale: 2, Precision: 12);
            public sealed class Pistol : Entity<Guid>
            {
                private Pistol() { }
                public int Number { get; private set; }
            }
            public sealed class Tank : Aggregate<TankId>, IVersioned
            {
                private Tank() { }
                public List<Pistol> Pistols { get; } = [];
                public DriverName Driver { get; private set; } = null!;
                public Litres Level { get; private set; } = null!;
                public string Label { get; private set; } = "";
                public string? Note { get; private set; }
                public int Count { get; private set; }
                public int? Reading { get; private set; }
                public static void Configure(EntityTypeBuilder<Tank> b)
                {
                    {{tank}}
                }
            }
            public sealed class Pump : Aggregate<PumpId>
            {
                private Pump() { }
                public TankId TankId { get; private set; }
                public TankId? SpareTankId { get; private set; }
                public static void Configure(EntityTypeBuilder<Pump> b)
                {
                    {{pump}}
                }
            }
            public static class Elsewhere
            {
                {{elsewhere}}
            }
            """);

    [Theory]
    [InlineData("b.HasMany(x => x.Pistols).WithOne().OnDelete(DeleteBehavior.Cascade);", "OnDelete(DeleteBehavior.Cascade)")]
    [InlineData("b.Property(x => x.Driver).HasMaxLength(100);", "HasMaxLength(100)")]
    [InlineData("b.Property(x => x.Level).HasPrecision(12, 2);", "HasPrecision(12, 2)")]
    [InlineData("b.Property(x => x.Level).HasPrecision(12);", "HasPrecision(12)")]
    [InlineData("b.Property(x => x.Label).IsRequired();", "IsRequired()")]
    [InlineData("b.Property(x => x.Count).IsRequired(true);", "IsRequired(true)")]
    [InlineData("b.HasKey(x => x.Id);", "HasKey(x => x.Id)")]
    [InlineData("b.Property(x => x.Id).ValueGeneratedNever();", "ValueGeneratedNever()")]
    [InlineData("b.Property(x => x.Version).IsConcurrencyToken();", "IsConcurrencyToken()")]
    [InlineData("b.Property(x => x.Id).HasConversion(v => v.Value, v => new TankId(v));", "HasConversion(v => v.Value, v => new TankId(v))")]
    [InlineData("b.Property(x => x.Driver).HasConversion(v => v.Value, v => new DriverName(v));", "HasConversion(v => v.Value, v => new DriverName(v))")]
    public async Task A_line_that_says_what_rask_does_anyway_is_RASK192(string tank, string restated)
    {
        var diagnostics = await Analyzed(tank);

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal("RASK192", diagnostic.Id);
        Assert.Contains(restated, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("." + restated, diagnostic.Flagged());
    }

    [Theory]
    [InlineData("b.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);")]
    [InlineData("b.HasOne<Tank>().WithMany().HasForeignKey(x => x.SpareTankId).OnDelete(DeleteBehavior.ClientSetNull);")]
    public async Task An_on_delete_equal_to_ef_cores_for_a_key_that_holds_no_child_is_RASK192(string pump)
    {
        var diagnostics = await Analyzed("", pump);

        var diagnostic = Assert.Single(diagnostics);

        Assert.StartsWith(".OnDelete(", diagnostic.Flagged(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("b.HasMany(x => x.Pistols).WithOne().OnDelete(DeleteBehavior.Restrict);")]
    [InlineData("b.Property(x => x.Driver).HasMaxLength(50);")]
    [InlineData("b.Property(x => x.Level).HasPrecision(12, 3);")]
    [InlineData("b.Property(x => x.Note).IsRequired();")]
    [InlineData("b.Property(x => x.Reading).IsRequired();")]
    [InlineData("b.Property(x => x.Label).IsRequired(false);")]
    [InlineData("b.HasKey(x => x.Id).HasName(\"tank_pkey\");")]
    [InlineData("b.HasKey(x => x.Label);")]
    [InlineData("b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Id).HasDefaultValueSql(\"newid()\");")]
    [InlineData("b.Property(x => x.Label).IsConcurrencyToken();")]
    [InlineData("b.Property(x => x.Id).HasConversion(v => v.Value.ToString(), v => new TankId(Guid.Parse(v)));")]
    public async Task A_line_that_says_more_than_the_default_is_left_alone(string tank)
    {
        var diagnostics = await Analyzed(tank);

        var none = diagnostics;

        Assert.Empty(none);
    }

    [Theory]
    [InlineData("b.Property(x => x.Id).ValueGeneratedNever();")]
    [InlineData("b.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.NoAction);")]
    [InlineData("b.HasOne<Tank>().WithMany().OnDelete(DeleteBehavior.Cascade);")]
    public async Task A_key_rask_generates_or_a_rule_it_cannot_read_is_left_alone(string pump)
    {
        var diagnostics = await Analyzed("", pump);

        var none = diagnostics;

        Assert.Empty(none);
    }

    [Fact]
    public async Task An_on_delete_is_compared_with_the_apps_own_delete_rule()
    {
        var elsewhere = "public static void Rules(ModelConfigurationBuilder c) => c.DeleteBehavior(children: DeleteBehavior.ClientCascade, others: DeleteBehavior.NoAction);";

        var restated = await Analyzed(
            "b.HasMany(x => x.Pistols).WithOne().OnDelete(DeleteBehavior.ClientCascade);",
            "b.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.NoAction);",
            elsewhere);
        var said = await Analyzed("b.HasMany(x => x.Pistols).WithOne().OnDelete(DeleteBehavior.Cascade);", elsewhere: elsewhere);

        Assert.Equal(2, restated.Count);
        Assert.Empty(said);
    }

    [Fact]
    public async Task A_delete_rule_the_analyzer_cannot_read_silences_every_on_delete()
    {
        var elsewhere = "public static void Rules(ModelConfigurationBuilder c, DeleteBehavior d) => c.DeleteBehavior(d, d);";

        var diagnostics = await Analyzed("b.HasMany(x => x.Pistols).WithOne().OnDelete(DeleteBehavior.Cascade);", elsewhere: elsewhere);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_project_that_changes_the_naming_pattern_still_hears_about_its_other_defaults()
    {
        var elsewhere = "public static void Rules(ModelConfigurationBuilder c) => c.TableNames(t => t.Class);";

        var diagnostics = await Analyzed("b.ToTable(\"tanks\"); b.Property(x => x.Label).IsRequired();", elsewhere: elsewhere);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(".IsRequired()", diagnostic.Flagged());
    }
}
