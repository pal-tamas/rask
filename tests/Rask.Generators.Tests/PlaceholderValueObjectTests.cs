using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>RASK150: a required value object of an aggregate that starts from a made-up value, and its quick-fix.</summary>
public class PlaceholderValueObjectTests
{
    // The generated half's Create, written by hand: the analyzer runs without the generator.
    private static string Source(string members) => $$"""
        using Rask.Data;
        using Rask.Values;
        namespace Shop;
        public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 100)
        {
            public static DriverName Create(string Value, string? field = null) => new(Value);
        }
        public sealed class Delivery : Aggregate<int>
        {
            private Delivery() { }
            {{members}}
        }
        public sealed class Note
        {
            public DriverName Name { get; private set; } = DriverName.Create("Unknown");
        }
        """;

    private static async Task<IReadOnlyList<Diagnostic>> Rask150(string members)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Source(members), new CSharpParseOptions(LanguageVersion.Latest), cancellationToken: TestContext.Current.CancellationToken)],
            GeneratorDriverFixture.BuildReferences().Add(GeneratorDriverFixture.AssemblyReference("Rask.Data")),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PlaceholderValueObjectAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. diagnostics.Where(static d => d.Id == "RASK150")];
    }

    [Fact]
    public async Task A_required_value_object_of_an_aggregate_made_in_its_initializer_is_RASK150()
    {
        var members = """public DriverName Driver { get; private set; } = DriverName.Create("Unknown");""";

        var found = await Rask150(members);

        var diagnostic = Assert.Single(found);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'Driver' on 'Delivery' starts as a made-up 'DriverName'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public DriverName Driver { get; private set; } = null!;")]
    [InlineData("public DriverName? Mate { get; private set; } = DriverName.Create(\"Nobody\");")]
    [InlineData("public DriverName Fixed { get; } = DriverName.Create(\"Always\");")]
    [InlineData("public DriverName Driver { get; private set; }")]
    public async Task No_value_yet_an_optional_one_or_one_that_never_changes_is_left_alone(string members)
    {
        var found = await Rask150(members);

        Assert.Empty(found);
    }

    [Fact]
    public async Task The_RASK150_fix_starts_it_as_null_forgiven()
    {
        var source = Source("""public DriverName Driver { get; private set; } = DriverName.Create("Unknown");""");

        var fixed_ = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new PlaceholderValueObjectAnalyzer(), new PlaceholderValueObjectCodeFixProvider(), "RASK150", source, "Rask.Data", "Rask.Cqrs");

        Assert.Contains("public DriverName Driver { get; private set; } = null!;", fixed_, StringComparison.Ordinal);
        Assert.Contains("public DriverName Name { get; private set; } = DriverName.Create(\"Unknown\");", fixed_, StringComparison.Ordinal);
    }
}
