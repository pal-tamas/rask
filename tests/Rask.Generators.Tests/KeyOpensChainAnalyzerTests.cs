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
///     RASK046 — Key has to open a COMPONENT's chain. Since #685 a keyed child is identified by its key
///     rather than by its position, which means settling that identity hands back the instance the key
///     owns and discards the one the entry built. A step written before Key is applied to the discarded
///     one: it compiles, it renders, and the value simply goes missing — and only once the list changes
///     shape, which is the worst time to find out.
/// </summary>
public class KeyOpensChainAnalyzerTests
{
    private const string Component = """
        namespace Demo
        {
            public sealed partial class Row : Rask.Core.Component
            {
                public string? Title { get; set; }
            }
        }
        """;

    [Fact]
    public async Task A_component_chain_that_sets_a_property_before_Key_is_reported()
    {
        var d = Assert.Single(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => Row.Title("a").Key(1);
                }
            }
            """));

        Assert.Equal("RASK046", d.Id);
        // Names the step that would be lost, so the fix needs no re-reading of the chain.
        Assert.Contains("'Title'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_component_chain_that_opens_with_Key_is_clean()
    {
        Assert.Empty(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => Row.Key(1).Title("a");
                }
            }
            """));
    }

    // The exemption that keeps this off the common call site. An element is re-specified in full every
    // render — whatever its chain does not name, the deferred reset puts back — so it is never claimed
    // and nothing written before its Key can be lost. `Div.Class("row").Key(i)` is written in its
    // hundreds across the samples and must stay exactly as it reads.
    [Fact]
    public async Task An_element_chain_with_Key_last_is_clean()
    {
        Assert.Empty(await AnalyzeAsync("""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => Div.Class("row").Key(1);
                }
            }
            """));
    }

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
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new KeyOpensChainAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        return [.. all.Where(d => d.Id == "RASK046")];
    }
}
