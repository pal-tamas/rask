using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK192 quick-fix: a name Rask's conventions give anyway comes out of the configuration.</summary>
public class ConventionRestatedCodeFixTests
{
    private static string Source(string configure, string attributes = "") => $$"""
        using System;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Fuel;
        {{attributes}}
        public sealed class Fueling : Aggregate<Guid>
        {
            private Fueling() { }
            public Guid PistolId { get; private set; }
            public DateTime CreatedAt { get; private set; }
        {{configure}}
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new ConventionRestatedAnalyzer(), new ConventionRestatedCodeFixProvider(), "RASK192", source,
            "Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational");

    [Fact]
    public async Task The_RASK192_fix_takes_the_call_out_of_its_chain()
    {
        var source = Source("""
                public static void Configure(EntityTypeBuilder<Fueling> b)
                {
                    b.Property(x => x.PistolId).HasColumnName("pistol_id").HasMaxLength(9);
                }
            """);

        var fixed_ = await Fix(source);

        Assert.Contains("b.Property(x => x.PistolId).HasMaxLength(9);", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("HasColumnName", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK192_fix_deletes_a_statement_left_saying_nothing()
    {
        var source = Source("""
                public static void Configure(EntityTypeBuilder<Fueling> b)
                {
                    b.ToTable("fuelings");
                    b.Property(x => x.PistolId).HasColumnName("pistol_id");
                    b.HasIndex(x => x.CreatedAt);
                }
            """);

        var fixed_ = await Fix(source);

        Assert.DoesNotContain("ToTable", fixed_, StringComparison.Ordinal);
        Assert.Contains("b.Property(x => x.PistolId).HasColumnName(\"pistol_id\");", fixed_, StringComparison.Ordinal);
        Assert.Contains("b.HasIndex(x => x.CreatedAt);", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK192_fix_leaves_a_mapping_of_a_property_ef_core_would_not_map_by_itself()
    {
        var source = Source("""
                internal int Litres { get; private set; }
                public static void Configure(EntityTypeBuilder<Fueling> b)
                {
                    b.Property(x => x.Litres).HasColumnName("litres");
                }
            """);

        var fixed_ = await Fix(source);

        Assert.Contains("b.Property(x => x.Litres);", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK192_fix_gives_an_expression_body_left_empty_a_block()
    {
        var source = Source("""
                public static void Configure(EntityTypeBuilder<Fueling> b) => b.ToTable("fuelings");
            """);

        var fixed_ = await Fix(source);

        Assert.DoesNotContain("ToTable", fixed_, StringComparison.Ordinal);
        Assert.Contains("Configure(EntityTypeBuilder<Fueling> b)", fixed_, StringComparison.Ordinal);
        Assert.Contains("{ }", fixed_.Replace("{\n}", "{ }", StringComparison.Ordinal).Replace("{}", "{ }", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK192_fix_takes_an_attribute_off_with_its_list()
    {
        var source = Source("""
                public static void Configure(EntityTypeBuilder<Fueling> b) { }
            """, attributes: "[Table(\"fuelings\")]");

        var fixed_ = await Fix(source);

        Assert.DoesNotContain("[Table", fixed_, StringComparison.Ordinal);
        Assert.Contains("public sealed class Fueling", fixed_, StringComparison.Ordinal);
    }
}
