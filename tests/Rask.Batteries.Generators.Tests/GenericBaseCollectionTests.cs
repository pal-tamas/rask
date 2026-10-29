namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A collection behind a private field of a GENERIC abstract base is generated for each closed derived aggregate
/// exactly as one a plain abstract base declares: the mapping, the form model and the read face.
/// </summary>
public class GenericBaseCollectionTests
{
    private const string Filings = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Tax;
        public sealed class FilingLine : Entity<int>
        {
            private FilingLine() { }
            public string Text { get; private set; } = "";
        }
        public abstract class Filing<TSelf> : Aggregate<int>, IFormModel, IDeletable
            where TSelf : Filing<TSelf>
        {
            private readonly List<FilingLine> _lines = [];
            private readonly List<string> _tags = [];
            public string Title { get; private set; } = "";
            public IReadOnlyList<FilingLine> Lines => _lines;
            public IReadOnlyList<string> Tags => _tags;
        }
        public sealed class VatReturn : Filing<VatReturn>
        {
            private VatReturn() { }
            public string Period { get; private set; } = "";
        }
        """;

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(
            Filings,
            [new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator()],
            "Rask.Data", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void The_generic_bases_field_collections_are_declared_for_the_closed_derived_aggregate()
    {
        var run = Run();

        var conventions = run.GeneratedSource("__RaskConventions");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "DeclareCollection(typeof(global::Tax.VatReturn), \"Tags\", \"_tags\", null);", conventions, StringComparison.Ordinal);
    }

    [Fact]
    public void The_form_model_of_the_closed_derived_aggregate_carries_the_generic_bases_collections()
    {
        var run = Run();

        var model = run.GeneratedSource("Tax.VatReturnModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(" Lines { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains(" Tags { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains(" Period { get; set; }", model, StringComparison.Ordinal);
    }

    [Fact]
    public void The_read_face_of_the_closed_derived_aggregate_holds_the_generic_bases_lines_and_values()
    {
        var run = Run();

        var face = run.GeneratedSource("Tax_VatReturnRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(" Lines { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("List<string> Tags { get; init; }", face, StringComparison.Ordinal);
    }
}
