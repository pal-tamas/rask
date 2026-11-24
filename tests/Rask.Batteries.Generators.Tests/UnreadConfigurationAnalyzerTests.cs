using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// RASK165: a KeepEmpty, Columns or IsUnique(message) the generators read from the entity's own static Configure,
/// written anywhere else or in a shape they cannot read.
/// </summary>
public sealed class UnreadConfigurationAnalyzerTests
{
    private static Task<IReadOnlyList<Diagnostic>> Analyzed(string configure, string elsewhere = "") =>
        ModelStateAnalyzerHarness.RunAsync(new UnreadConfigurationAnalyzer(), $$"""
            using System;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Shop;
            public sealed partial record VatNumber(string Value) : Text(Value, MaxLength: 13);
            public sealed partial record KmRange(int Start, int End) : Rask.Data.Range(Start, End);
            public sealed class Company : Aggregate<Guid>
            {
                private Company() { }
                public VatNumber? Vat { get; private set; }
                public string Name { get; private set; } = "";
                public KmRange Km { get; private set; } = null!;
                public static void Configure(EntityTypeBuilder<Company> b)
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
    [InlineData("b.KeepEmpty(c => c.Vat);")]
    [InlineData("b.Columns(c => c.Km, \"{Member}Km\");")]
    [InlineData("const string Pattern = \"{Member}Km\"; b.Columns(c => c.Km, Pattern);")]
    [InlineData("b.HasIndex(c => c.Name).IsUnique(\"That name is taken.\");")]
    [InlineData("b.HasIndex(c => c.Name).IsUnique();")]
    public async Task A_call_written_directly_in_the_entitys_own_Configure_is_fine(string configure)
    {
        var diagnostics = await Analyzed(configure);

        var none = diagnostics;

        Assert.Empty(none);
    }

    [Theory]
    [InlineData("Elsewhere.Keep(b);", "public static void Keep(EntityTypeBuilder<Company> b) => b.KeepEmpty(c => c.Vat);", "'KeepEmpty' configures 'Company'", "made outside that method")]
    [InlineData("Elsewhere.Name(b);", "public static void Name(EntityTypeBuilder<Company> b) => b.Columns(c => c.Km, \"{Member}Km\");", "'Columns' configures 'Company'", "made outside that method")]
    [InlineData("Elsewhere.Unique(b);", "public static void Unique(EntityTypeBuilder<Company> b) => b.HasIndex(c => c.Name).IsUnique(\"Taken.\");", "'IsUnique' configures 'Company'", "made outside that method")]
    [InlineData("KeepEmptyBuilderExtensions.KeepEmpty(b, c => c.Vat);", "", "'KeepEmpty'", "called as a static method")]
    [InlineData("var pattern = \"{Member}Km\"; b.Columns(c => c.Km, pattern);", "", "'Columns'", "its pattern is not a constant")]
    [InlineData("System.Linq.Expressions.Expression<Func<Company, VatNumber?>> vat = c => c.Vat; b.KeepEmpty(vat);", "", "'KeepEmpty'", "not a lambda naming a property")]
    public async Task A_call_the_generators_cannot_read_is_RASK165(string configure, string elsewhere, string named, string why)
    {
        var diagnostics = await Analyzed(configure, elsewhere);

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal("RASK165", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(named, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(why, diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
