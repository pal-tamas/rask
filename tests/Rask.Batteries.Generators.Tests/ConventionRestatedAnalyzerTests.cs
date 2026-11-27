using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK192: a name stated in Configure that is exactly the one Rask's conventions give.</summary>
public sealed class ConventionRestatedAnalyzerTests
{
    private static Task<IReadOnlyList<Diagnostic>> Analyzed(string configure, string attributes = "", string elsewhere = "") =>
        ModelStateAnalyzerHarness.RunAsync(new ConventionRestatedAnalyzer(), $$"""
            using System;
            using System.ComponentModel.DataAnnotations.Schema;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Fuel;
            public sealed record Odometer(int Start, int End);
            public sealed class Pistol : Aggregate<Guid>
            {
                private Pistol() { }
                public int Number { get; private set; }
            }
            {{attributes}}
            public sealed class Fueling : Aggregate<Guid>
            {
                private Fueling() { }
                public Guid PistolId { get; private set; }
                public DateTime CreatedAt { get; private set; }
                public Odometer Km { get; private set; } = null!;
                public static void Configure(EntityTypeBuilder<Fueling> b)
                {
                    {{configure}}
                }
            }
            public static class Elsewhere
            {
                {{elsewhere}}
            }
            """);

    [Theory]
    [InlineData("b.ToTable(\"fuelings\");", "ToTable(\"fuelings\")")]
    [InlineData("b.Property(x => x.PistolId).HasColumnName(\"pistol_id\");", "HasColumnName(\"pistol_id\")")]
    [InlineData("b.Property(x => x.CreatedAt).IsRequired().HasColumnName(\"created_at\");", "HasColumnName(\"created_at\")")]
    [InlineData("b.ComplexProperty(x => x.Km).Property(k => k.Start).HasColumnName(\"km_start\");", "HasColumnName(\"km_start\")")]
    [InlineData("b.ComplexProperty(x => x.Km, k => k.Property(o => o.End).HasColumnName(\"km_end\"));", "HasColumnName(\"km_end\")")]
    [InlineData("b.HasIndex(x => x.CreatedAt).HasDatabaseName(\"ix_fuelings_created_at\");", "HasDatabaseName(\"ix_fuelings_created_at\")")]
    [InlineData("b.HasIndex(x => new { x.PistolId, x.CreatedAt }).IsUnique().HasDatabaseName(\"uq_fuelings_pistol_id_created_at\");", "HasDatabaseName(\"uq_fuelings_pistol_id_created_at\")")]
    [InlineData("b.HasIndex(\"PistolId\").HasDatabaseName(\"ix_fuelings_pistol_id\");", "HasDatabaseName(\"ix_fuelings_pistol_id\")")]
    [InlineData("b.HasKey(x => x.Id).HasName(\"pk_fuelings\");", "HasName(\"pk_fuelings\")")]
    [InlineData("b.HasOne<Pistol>().WithMany().HasForeignKey(x => x.PistolId).HasConstraintName(\"fk_fuelings_pistol_id\");", "HasConstraintName(\"fk_fuelings_pistol_id\")")]
    public async Task A_name_rask_would_give_anyway_is_RASK192(string configure, string named)
    {
        var diagnostics = await Analyzed(configure);

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal("RASK192", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains(WellKnownDiagnosticTags.Unnecessary, diagnostic.Descriptor.CustomTags);
        Assert.Contains(named, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("." + named, diagnostic.Flagged());
    }

    [Theory]
    [InlineData("b.ToTable(\"Fueling\");")]
    [InlineData("b.ToTable(\"fuelings\", \"dbo\");")]
    [InlineData("b.Property(x => x.PistolId).HasColumnName(\"pistolId\");")]
    [InlineData("b.HasIndex(x => x.CreatedAt).HasDatabaseName(\"IX_Fueling_CreatedAt\");")]
    [InlineData("b.HasIndex(x => x.CreatedAt).IsUnique().HasDatabaseName(\"ix_fuelings_created_at\");")]
    [InlineData("b.HasIndex(x => x.Km).HasDatabaseName(\"ix_fuelings_km\");")]
    [InlineData("b.ToTable(\"legacy\"); b.HasIndex(x => x.CreatedAt).HasDatabaseName(\"ix_fuelings_created_at\");")]
    public async Task A_name_rask_would_not_give_is_left_alone(string configure)
    {
        var diagnostics = await Analyzed(configure);

        var none = diagnostics;

        Assert.Empty(none);
    }

    [Fact]
    public async Task An_index_on_a_table_named_otherwise_is_compared_with_that_tables_name()
    {
        var diagnostics = await Analyzed("b.ToTable(\"legacy\"); b.HasIndex(x => x.CreatedAt).HasDatabaseName(\"ix_legacy_created_at\");");

        var diagnostic = Assert.Single(diagnostics);

        Assert.Contains("ix_legacy_created_at", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_foreign_key_held_by_another_entity_is_left_alone()
    {
        var elsewhere = "public static void Map(EntityTypeBuilder<Pistol> b) => b.HasMany<Fueling>().WithOne().HasForeignKey(x => x.PistolId).HasConstraintName(\"fk_fuelings_pistol_id\");";

        var diagnostics = await Analyzed("", elsewhere: elsewhere);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_table_attribute_rask_would_give_anyway_is_RASK192()
    {
        var diagnostics = await Analyzed("", attributes: "[Table(\"fuelings\")]");

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal("[Table(\"fuelings\")]", "[" + diagnostic.Flagged() + "]");
    }

    [Fact]
    public async Task A_project_that_changes_the_pattern_gets_no_naming_report()
    {
        var elsewhere = "public static void Rules(ModelConfigurationBuilder c) => c.TableNames(t => t.Class);";

        var diagnostics = await Analyzed("b.ToTable(\"fuelings\");", elsewhere: elsewhere);

        Assert.Empty(diagnostics);
    }
}
