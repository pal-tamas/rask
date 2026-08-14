using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;
using Xunit;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK044 — a chain that writes the same property twice. The second call wins and the first is dead
///     code that renders nothing; the compiler is perfectly happy with it, which is the whole reason this
///     exists.
/// </summary>
public class DuplicateChainCallAnalyzerTests
{
    private const string Component = """
        namespace Demo
        {
            public sealed partial class Card : Rask.Core.Component
            {
                public string? Title { get; set; }
                public string? Note { get; set; }
            }
        }
        """;

    [Fact]
    public async Task A_chain_that_sets_one_property_twice_is_reported()
    {
        var d = Assert.Single(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => Card.Title("a").Note("n").Title("b");
                }
            }
            """));

        Assert.Equal("RASK044", d.Id);
        Assert.Contains("'Title'", d.GetMessage(), StringComparison.Ordinal);
    }

    // Once per chain, not once per link — and the name is the property, so the fix is obvious without
    // reading the chain back.
    [Fact]
    public async Task A_property_written_three_times_is_reported_once()
    {
        var d = Assert.Single(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => Card.Title("a").Title("b").Title("c");
                }
            }
            """));

        Assert.Equal("RASK044", d.Id);
    }

    [Fact]
    public async Task A_chain_that_sets_each_property_once_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => Card.Title("a").Note("n");
                }
            }
            """));

    // Two chains in one expression are two chains: the same property named once in each is not a
    // duplicate, and reporting it would fire on every list of siblings that share a class.
    [Fact]
    public async Task The_same_property_on_two_separate_chains_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() =>
                        Div[Card.Title("a"), Card.Title("b")];
                }
            }
            """));

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string page)
    {
        var run = BuilderGeneratorHarness.Run(Component + "\n" + page);
        var trees = run.Sources
            .Select(s => s.SourceText.ToString())
            .Prepend(Component + "\n" + page)
            .Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DuplicateChainCallAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        return [.. all.Where(d => d.Id == "RASK044")];
    }
}
