using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;
using Xunit;

namespace Rask.Generators.Tests;

// Several analyzers identify their subject as "a static method on the class named Generated" — the
// FACTORY. Markup is now a chain, where the same markup is a property reference plus extension setters
// and a children indexer, with no such method anywhere. An analyzer written that way does not fail on the
// chain; it simply never fires, which is the worst way for a check to stop working.
//
// Their existing test files compile without running the builder generator, so they cannot express a chain
// at all and could not have caught this. These run the generator first.
public class AnalyzersOnTheChainSurfaceTests
{
    [Fact]
    public async Task A_keyless_chain_built_list_reports_Rask022()
    {
        var d = await AnalyzeAsync(
            """
            using System.Linq;
            using Rask.Core;
            namespace Demo
            {
                public sealed partial class App : Component
                {
                    private readonly int[] _items = { 1, 2, 3 };

                    protected override Component? Render() =>
                        Ul[_items.Select(i => (Component)Li[i.ToString()])];
                }
            }
            """,
            "RASK022",
            new MissingKeyAnalyzer());

        Assert.NotEmpty(d);
    }

    [Fact]
    public async Task A_keyed_chain_built_list_is_clean()
    {
        Assert.Empty(await AnalyzeAsync(
            """
            using System.Linq;
            using Rask.Core;
            namespace Demo
            {
                public sealed partial class App : Component
                {
                    private readonly int[] _items = { 1, 2, 3 };

                    protected override Component? Render() =>
                        Ul[_items.Select(i => (Component)Li.Key(i)[i.ToString()])];
                }
            }
            """,
            "RASK022",
            new MissingKeyAnalyzer()));
    }

    [Fact]
    public async Task An_alt_less_chain_built_img_reports_Rask023()
    {
        var d = await AnalyzeAsync(
            """
            using Rask.Core;
            namespace Demo
            {
                public sealed partial class App : Component
                {
                    protected override Component? Render() => Img.Src("/logo.png");
                }
            }
            """,
            "RASK023",
            new ImgMissingAltAnalyzer());

        Assert.NotEmpty(d);
    }

    [Fact]
    public async Task A_chain_built_img_with_alt_is_clean()
    {
        Assert.Empty(await AnalyzeAsync(
            """
            using Rask.Core;
            namespace Demo
            {
                public sealed partial class App : Component
                {
                    protected override Component? Render() => Img.Src("/logo.png").Alt("A logo");
                }
            }
            """,
            "RASK023",
            new ImgMissingAltAnalyzer()));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source, string id, DiagnosticAnalyzer analyzer)
    {
        var run = BuilderGeneratorHarness.Run(source);
        var trees = run.Sources
            .Select(s => s.SourceText.ToString())
            .Prepend(source)
            .Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();

        return [.. all.Where(d => d.Id == id)];
    }
}
