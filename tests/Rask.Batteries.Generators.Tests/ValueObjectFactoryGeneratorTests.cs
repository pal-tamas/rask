using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A value object declared from a base record — <c>record DriverName() : Text(MaxLength: 1000)</c>: its generated
/// half, how a generated model builds and asks it, and RASK127–129.
/// </summary>
public class ValueObjectFactoryGeneratorTests
{
    private const string Usings = """
        using System;
        using Rask.Data;
        namespace Shop;

        """;

    private const string ValueObjects = """
        public sealed partial record DriverName() : Text(MaxLength: 1000);
        public sealed partial record Litres() : Number(Min: 1, Scale: 2);
        public sealed partial record Pieces() : Whole(Min: 0, Max: 1000);
        public sealed partial record InvoiceDay() : Day(NotAfterToday: true);

        """;

    private const string Holder = """
        public sealed class Delivery : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
        {
            private Delivery() { }
            public DriverName Driver { get; private set; } = DriverName.Create("x");
            public DriverName? Mate { get; private set; }
            public Litres Fuel { get; private set; } = Litres.Create(1);
            public Pieces Count { get; private set; } = Pieces.Create(0);
            public InvoiceDay? Invoiced { get; private set; }
        }
        """;

    private static GeneratorRun Run(string source, params string[] extra) =>
        GeneratorHarness.Run(
            Usings + source,
            [new ValueObjectFactoryGenerator(), new ModelInputGenerator()],
            ["Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore", .. extra]);

    [Theory]
    [InlineData("DriverName", "string")]
    [InlineData("Litres", "decimal")]
    [InlineData("Pieces", "long")]
    [InlineData("InvoiceDay", "global::System.DateOnly")]
    public void Each_base_gets_its_constructor_and_factories_for_the_value_it_holds(string name, string held)
    {
        var run = Run(ValueObjects);
        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains($"private {name}({held} value) : this() => Value = value;", generated, StringComparison.Ordinal);
        Assert.Contains($"public static {name} Create({held}? value, string? field = null)", generated, StringComparison.Ordinal);
        Assert.Contains($"public static bool TryCreate({held}? value,", generated, StringComparison.Ordinal);
        Assert.Contains($"public static {name}? CreateOptional({held}? value, string? field = null)", generated, StringComparison.Ordinal);
        Assert.Contains($"__Problems({held}? value)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Messages_are_worded_from_the_catalog_where_the_compilation_has_one()
    {
        var withCatalog = Run(ValueObjects, "Rask.Core").GeneratedSource("Litres.Value.g.cs");
        var without = GeneratorHarness.RunWithout(
                Usings + ValueObjects, new ValueObjectFactoryGenerator(), ["Rask.Core"], "Rask.Data", "Rask.Cqrs", "Rask.Wire")
            .GeneratedSource("Litres.Value.g.cs");

        Assert.Contains("RaskString.ValidationNumberMin, problem.Template, problem.Limit", withCatalog, StringComparison.Ordinal);
        Assert.Contains("RaskString.ValidationDecimals", withCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("RaskStrings", without, StringComparison.Ordinal);
        Assert.Empty(Run(ValueObjects, "Rask.Core").GeneratedCompileErrors());
    }

    [Fact]
    public void A_value_object_nested_in_a_partial_type_is_generated_inside_it()
    {
        var run = Run("""
            public static partial class Fleet
            {
                public sealed partial record Plate() : Text(MaxLength: 10);
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("partial class Fleet", run.GeneratedSource("Plate.Value.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_generated_model_asks_the_generated_rule_and_builds_through_Create()
    {
        var run = Run(ValueObjects + Holder);
        var model = run.GeneratedSource("DeliveryModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK102");
        Assert.Contains("global::Shop.DriverName.__Problems(held)", model, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(value) ? global::System.Array.Empty<string>() : global::Shop.DriverName.__Problems(value)", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.Litres.__Problems(held)", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.DriverName.Create(", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.DriverName.CreateOptional(", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.InvoiceDay.CreateOptional(", model, StringComparison.Ordinal);
        Assert.DoesNotContain("new global::Shop.DriverName", model, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public partial record Plate() : Text(MaxLength: 10);", "'sealed'")]
    [InlineData("public sealed record Plate() : Text(MaxLength: 10);", "'partial'")]
    [InlineData("public record Plate() : Text(MaxLength: 10);", "'sealed partial'")]
    public void A_record_that_is_not_sealed_partial_is_RASK129(string declaration, string lacks)
    {
        var run = Run(declaration);

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK129");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Plate' is declared from Rask.Data.Text, so it must be " + lacks, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.RunResult.Results.SelectMany(r => r.GeneratedSources), s => s.HintName.Contains("Plate", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_object_nested_in_a_type_that_is_not_partial_is_RASK129()
    {
        var run = Run("""
            public static class Fleet
            {
                public sealed partial record Plate() : Text(MaxLength: 10);
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK129");
        Assert.Contains("'Fleet', which it is nested in", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_sealed_partial_record_and_an_ordinary_record_are_not_RASK129()
    {
        var run = Run(ValueObjects + "public record Other(string Value);");

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK129");
    }

    // ---- RASK127 ----

    private const string Plate = """
        using Rask.Data;
        namespace Shop;
        public sealed partial record Plate() : Text(MaxLength: 10)
        {
            public static Plate Blank() => new();
            public Plate Same() => this with { };
        }
        public static class Elsewhere
        {
            ELSEWHERE
        }
        """;

    [Fact]
    public async Task A_base_record_value_object_made_with_new_outside_its_type_is_RASK127()
    {
        var diagnostic = Assert.Single(await ModelStateAnalyzerHarness.RunAsync(
            new ValueObjectCreationAnalyzer(), Plate.Replace("ELSEWHERE", "public static Plate Make() => new Plate();", StringComparison.Ordinal)));

        Assert.Equal("RASK127", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Plate' is declared from Rask.Data.Text, so it is made only by 'Plate.Create(…)'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_with_outside_its_type_is_RASK127()
    {
        var diagnostic = Assert.Single(await ModelStateAnalyzerHarness.RunAsync(
            new ValueObjectCreationAnalyzer(), Plate.Replace("ELSEWHERE", "public static Plate Copy(Plate p) => p with { };", StringComparison.Ordinal)));

        Assert.Contains("instead of 'with'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_and_with_inside_its_own_type_and_on_other_records_are_fine()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new ValueObjectCreationAnalyzer(),
            Plate.Replace("ELSEWHERE", "public record Other(string Value); public static Other Make() => new Other(\"a\") with { };", StringComparison.Ordinal));

        Assert.Empty(diagnostics);
    }

    // ---- RASK128 ----

    private static Task<IReadOnlyList<Diagnostic>> Configured(string configure) =>
        ModelStateAnalyzerHarness.RunAsync(new ValueObjectColumnFacetAnalyzer(), $$"""
            using System;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Shop;
            public sealed partial record DriverName() : Text(MaxLength: 1000);
            public sealed partial record Litres() : Number(Min: 1, Scale: 2, Precision: 9);
            public sealed partial record Note() : Text(MinLength: 2);
            public sealed class Delivery : Aggregate<Guid>
            {
                private Delivery() { }
                public DriverName Driver { get; private set; } = null!;
                public Litres Fuel { get; private set; } = null!;
                public Note Remark { get; private set; } = null!;
                public static void Configure(EntityTypeBuilder<Delivery> builder)
                {
                    {{configure}}
                }
            }
            """);

    [Theory]
    [InlineData("builder.Property(d => d.Driver).HasMaxLength(200);", "declares MaxLength: 1000, but 'HasMaxLength' gives its column a length of 200")]
    [InlineData("builder.Property(d => d.Driver).HasColumnType(\"nvarchar(max)\");", "gives its column no length")]
    [InlineData("builder.Property(d => d.Fuel).HasPrecision(18, 4);", "declares Precision: 9, Scale: 2, but 'HasPrecision' gives its column a precision of 18 and a scale of 4")]
    [InlineData("builder.Property(d => d.Fuel).HasColumnType(\"decimal(9,3)\");", "a precision of 9 and a scale of 3")]
    public async Task A_configured_facet_other_than_the_value_objects_is_RASK128(string configure, string said)
    {
        var diagnostic = Assert.Single(await Configured(configure));

        Assert.Equal("RASK128", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(said, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("builder.Property(d => d.Driver).HasMaxLength(1000);")]
    [InlineData("builder.Property(d => d.Fuel).HasPrecision(9, 2);")]
    [InlineData("builder.Property(d => d.Fuel).HasColumnType(\"decimal(9, 2)\");")]
    [InlineData("builder.Property(d => d.Remark).HasMaxLength(50);")]
    [InlineData("var length = 200; builder.Property(d => d.Driver).HasMaxLength(length);")]
    public async Task A_configured_facet_that_agrees_or_is_not_literal_is_fine(string configure)
    {
        Assert.Empty(await Configured(configure));
    }
}
