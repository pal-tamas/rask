namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// What an abstract base declares — at any depth — is generated for every aggregate deriving from it exactly as
/// what the aggregate declares itself: the column mapping, the read face and the form model.
/// </summary>
public class InheritedValueObjectTests
{
    private const string Filings = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Tax;
        public readonly record struct Barcode(string Value);
        public sealed record Office(string Value);
        public sealed record Period(int Year, int Quarter);
        public sealed record Stamp(string By, int Day);
        public sealed class Line : Entity<int>
        {
            private Line() { }
            public string Text { get; private set; } = "";
        }
        public abstract class Filing : Aggregate<int>, IFormModel
        {
            private readonly List<string> _tags = [];
            private readonly List<Line> _lines = [];
            public Barcode? Barcode { get; private set; }
            public Office Office { get; private set; } = new("");
            public Period Period { get; private set; } = new(2026, 1);
            public IReadOnlyList<string> Tags => _tags;
            public IReadOnlyList<Stamp> Stamps { get; private set; } = [];
            public IReadOnlyList<Line> Lines => _lines;
        }
        public abstract class TaxFiling : Filing
        {
            public Office? Receipt { get; private set; }
        }
        public sealed class GasFiling : TaxFiling
        {
            private GasFiling() { }
            public int Litres { get; private set; }
        }
        public sealed class ForeignFiling : Filing
        {
            private ForeignFiling() { }
        }
        """;

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(
            Filings,
            [new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator()],
            "Rask.Data", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void Each_derived_aggregate_maps_the_value_objects_its_bases_declare()
    {
        var run = Run();

        var registry = run.GeneratedSource("__RaskModelRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        foreach (var local in new[] { "__Tax_GasFiling", "__Tax_ForeignFiling" })
        {
            Assert.Contains(
                $"global::Rask.Data.ValueObjectColumn.Map({local}.Property(x => x.Barcode), (global::Tax.Barcode v) => v.Value);",
                registry,
                StringComparison.Ordinal);
            Assert.Contains(
                $"global::Rask.Data.ValueObjectColumn.Map({local}.Property(x => x.Office), (global::Tax.Office v) => v.Value);",
                registry,
                StringComparison.Ordinal);
            Assert.Contains($"{local}.ComplexProperty(x => x.Period);", registry, StringComparison.Ordinal);
        }

        Assert.Contains(
            "global::Rask.Data.ValueObjectColumn.Map(__Tax_GasFiling.Property(x => x.Receipt), (global::Tax.Office v) => v.Value);",
            registry,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_collections_and_children_a_base_declares_are_declared_for_each_derived_aggregate()
    {
        var run = Run();

        var conventions = run.GeneratedSource("__RaskConventions");

        Assert.Contains(
            "DeclareCollection(typeof(global::Tax.GasFiling), \"Tags\", \"_tags\", null);", conventions, StringComparison.Ordinal);
        Assert.Contains(
            "DeclareCollection(typeof(global::Tax.ForeignFiling), \"Stamps\", null, typeof(global::Tax.Stamp));",
            conventions,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_form_model_of_a_derived_aggregate_carries_every_value_its_bases_hold()
    {
        var run = Run();

        var model = run.GeneratedSource("Tax.GasFilingModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("    public string? Barcode { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains(" Office { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains("    public string? Receipt { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains(" Litres { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains(" Tags { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains(" Lines { get; set; }", model, StringComparison.Ordinal);
        // The field is the base's, so its accessor is declared against the base: UnsafeAccessor does not look up.
        Assert.Contains(
            "private static extern ref global::System.Collections.Generic.List<string> __Values_Tags(global::Tax.Filing entity);",
            model,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_read_face_of_a_derived_aggregate_shows_the_bases_values_as_the_primitives_they_hold()
    {
        var run = Run();

        var face = run.GeneratedSource("Tax_GasFilingRead");

        Assert.Contains("public string? Barcode { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public string Office { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public string? Receipt { get; init; }", face, StringComparison.Ordinal);
    }
}
