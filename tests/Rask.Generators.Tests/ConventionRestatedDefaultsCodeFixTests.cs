using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK192 quick-fix on a default restated: the call comes out, and a statement left saying nothing with it.</summary>
public class ConventionRestatedDefaultsCodeFixTests
{
    private static string Source(string configure) => $$"""
        using System;
        using System.Collections.Generic;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        using Rask.Values;
        namespace Fuel;
        public readonly partial record struct TankId(Guid Value);
        public sealed record DriverName(string Value) : Text(Value, MaxLength: 100);
        public sealed class Pistol : Entity<Guid>
        {
            private Pistol() { }
        }
        public sealed class Tank : Aggregate<TankId>, IVersioned
        {
            private Tank() { }
            public List<Pistol> Pistols { get; } = [];
            public DriverName Driver { get; private set; } = null!;
            public string Label { get; private set; } = "";
            public static void Configure(EntityTypeBuilder<Tank> b)
            {
                {{configure}}
                b.Property(x => x.Label).HasMaxLength(9);
            }
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new ConventionRestatedAnalyzer(), new ConventionRestatedCodeFixProvider(), "RASK192", source,
            "Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational");

    [Theory]
    [InlineData("b.HasMany(x => x.Pistols).WithOne().OnDelete(DeleteBehavior.Cascade);", "b.HasMany(x => x.Pistols).WithOne();")]
    [InlineData("b.Property(x => x.Driver).HasMaxLength(100).HasColumnName(\"name\");", "b.Property(x => x.Driver).HasColumnName(\"name\");")]
    [InlineData("b.Property(x => x.Label).IsRequired().HasColumnName(\"title\");", "b.Property(x => x.Label).HasColumnName(\"title\");")]
    public async Task The_RASK192_fix_takes_a_restated_default_out_of_its_chain(string configure, string kept)
    {
        var source = Source(configure);

        var fixed_ = await Fix(source);

        Assert.Contains(kept, fixed_, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("b.HasKey(x => x.Id);", "HasKey")]
    [InlineData("b.Property(x => x.Id).ValueGeneratedNever();", "ValueGeneratedNever")]
    [InlineData("b.Property(x => x.Version).IsConcurrencyToken();", "IsConcurrencyToken")]
    [InlineData("b.Property(x => x.Id).HasConversion(v => v.Value, v => new TankId(v));", "HasConversion")]
    [InlineData("b.Property(x => x.Label).IsRequired();", "IsRequired")]
    public async Task The_RASK192_fix_deletes_a_statement_that_only_restated_a_default(string configure, string gone)
    {
        var source = Source(configure);

        var fixed_ = await Fix(source);

        Assert.DoesNotContain(gone, fixed_, StringComparison.Ordinal);
        Assert.Contains("b.Property(x => x.Label).HasMaxLength(9);", fixed_, StringComparison.Ordinal);
    }
}
