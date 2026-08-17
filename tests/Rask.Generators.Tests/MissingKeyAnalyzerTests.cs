using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class MissingKeyAnalyzerTests
{
    // Wraps a Render() body in a component. Real Rask.Core factories (Generated.Li/Tr/Ul/...) are
    // referenced via BuildReferences(), so the analyzer resolves genuine factory symbols.
    private static string App(string body) => $$"""
                                                using System.Collections.Generic;
                                                using System.Linq;
                                                using Rask.Core;
                                                using static Rask.Core.Components.Generated;
                                                using static Rask.Html.Components.Generated;
                                                namespace Demo;
                                                public sealed partial class App : Component
                                                {
                                                    private readonly int[] _items = { 1, 2, 3 };
                                                    protected override Component? Render()
                                                    {
                                                        {{body}}
                                                    }
                                                }
                                                """;

    // The chain is what the framework teaches, so keyless-list detection has to see it. A chain's steps
    // are extension methods on Build<T>, not a static Generated.Li(...), so the factory branch matched
    // none of these and the warning was silently absent from every chain ever written.
    [Fact]
    public async Task ChainSelectProjection_NoKey_ReportsRask022()
    {
        var d = Assert.Single(await Diagnostics(App(
            "return Ul[ _items.Select(i => Li[i.ToString()]) ];")));
        Assert.Equal("RASK022", d.Id);
    }

    [Fact]
    public async Task ChainSelectProjection_WithKey_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App(
            "return Ul[ _items.Select(i => Li.Key(i)[i.ToString()]) ];")));

    [Fact]
    public async Task SelectProjection_NoKey_ReportsRask022()
    {
        var d = Assert.Single(await Diagnostics(App(
            "return Ul()[ _items.Select(i => Li()[i.ToString()]) ];")));
        Assert.Equal("RASK022", d.Id);
        Assert.Contains("Li", d.GetMessage());
    }

    [Fact]
    public async Task ForeachAddToChildList_NoKey_ReportsRask022()
    {
        var d = Assert.Single(await Diagnostics(App("""
                                                    var rows = new List<Component>();
                                                    foreach (var i in _items) rows.Add(Tr()[i.ToString()]);
                                                    return Ul()[rows];
                                                    """)));
        Assert.Equal("RASK022", d.Id);
        Assert.Contains("Tr", d.GetMessage());
    }

    [Fact]
    public async Task SelectProjection_WithKey_NoDiagnostic()
    {
        Assert.Empty(await Diagnostics(App(
            "return Ul()[ _items.Select(i => Li(Key: i)[i.ToString()]) ];")));
    }

    [Fact]
    public async Task SelectProjection_WithDataRaskKey_NoDiagnostic()
    {
        Assert.Empty(await Diagnostics(App("""
                                           return Ul()[ _items.Select(i => Li(
                                               Data: new Dictionary<string, string?> { ["rask-key"] = i.ToString() })[i.ToString()]) ];
                                           """)));
    }

    [Fact]
    public async Task ForeachAddToChildList_WithKey_NoDiagnostic()
    {
        Assert.Empty(await Diagnostics(App("""
                                           var rows = new List<Component>();
                                           foreach (var i in _items) rows.Add(Tr(Key: i)[i.ToString()]);
                                           return Ul()[rows];
                                           """)));
    }

    [Fact]
    public async Task NestedChildOfProjectedItem_OnlyOuterItemFlagged()
    {
        // Li is the projected list item (flagged once); the nested Code is Li's child, not a
        // sibling in the reconciled list, so it must NOT be flagged.
        var d = Assert.Single(await Diagnostics(App(
            "return Ul()[ _items.Select(i => Li()[ Code()[i.ToString()] ]) ];")));
        Assert.Contains("Li", d.GetMessage());
    }

    [Fact]
    public async Task SingleStaticChild_NotAList_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Div()[ Span()[\"hi\"] ];")));

    [Fact]
    public async Task AddOutsideLoop_NoDiagnostic()
    {
        // A one-off Add (not in a loop) isn't a reconciled list — don't warn.
        Assert.Empty(await Diagnostics(App("""
                                           var rows = new List<Component>();
                                           rows.Add(Tr()["one"]);
                                           return Ul()[rows];
                                           """)));
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MissingKeyAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK022").ToImmutableArray();
    }
}
