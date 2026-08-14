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
///     RASK045 — a component built by a chain, written to afterwards. The chain is meant to state
///     everything a component was given, in one expression the reader of the call site can see; a later
///     assignment is a second source of truth, and the two can disagree.
///     <para>
///         It can only be an analyzer. <c>Build&lt;T&gt;</c> converts implicitly to the component it
///         built — which is exactly what keeps the chain out of the way at every call site that wants the
///         component itself — and once it has converted, the result is an ordinary component with
///         ordinary settable properties. Nothing in the type system is left to forbid the write.
///     </para>
/// </summary>
public class ChainMutationAnalyzerTests
{
    private const string Component = """
        namespace Demo
        {
            public sealed partial class Card : Rask.Core.Component
            {
                public string? Note { get; set; }
            }
        }
        """;

    [Fact]
    public async Task A_local_built_by_a_chain_and_then_written_to_is_reported()
    {
        var d = Assert.Single(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render()
                    {
                        Card c = Card.Note("a");
                        c.Note = "b";
                        return c;
                    }
                }
            }
            """));

        Assert.Equal("RASK045", d.Id);
        Assert.Contains("Note", d.GetMessage(), StringComparison.Ordinal);
    }

    // Written in place, through the implicit conversion the chain ends with.
    [Fact]
    public async Task A_chain_written_to_in_place_is_reported() =>
        Assert.Equal("RASK045", Assert.Single(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render()
                    {
                        ((Card)Card.Note("a")).Note = "b";
                        return null;
                    }
                }
            }
            """)).Id);

    // The chain saying everything is the point, so there is nothing to report.
    [Fact]
    public async Task A_chain_that_sets_everything_in_one_expression_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => Card.Note("a");
                }
            }
            """));

    // A component the chain did not build is not this rule's business: only a chain promises to be the
    // whole story, so only a chain's product is held to it.
    [Fact]
    public async Task A_component_built_by_new_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
            #pragma warning disable RASK014
                    protected override Rask.Core.Component? Render()
                    {
                        var c = new Card();
                        c.Note = "b";
                        return c;
                    }
            #pragma warning restore RASK014
                }
            }
            """));

    // A component held in a field and written from somewhere the chain never ran.
    [Fact]
    public async Task A_field_written_from_elsewhere_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    private Card? _kept;

                    internal void Touch()
                    {
                        if (_kept is not null)
                        {
                            _kept.Note = "b";
                        }
                    }

                    protected override Rask.Core.Component? Render() => null;
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
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ChainMutationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        return [.. all.Where(d => d.Id == "RASK045")];
    }
}
