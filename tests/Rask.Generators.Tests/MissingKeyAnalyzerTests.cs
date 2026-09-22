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
    public async Task A_chain_in_a_Select_projection_with_no_key_reports_RASK022()
    {
        var d = Assert.Single(await Diagnostics(App(
            "return Ul[ _items.Select(i => Li[i.ToString()]) ];")));
        Assert.Equal("RASK022", d.Id);
    }

    [Fact]
    public async Task A_chain_in_a_Select_projection_with_a_key_reports_nothing() =>
        Assert.Empty(await Diagnostics(App(
            "return Ul[ _items.Select(i => Li.Key(i)[i.ToString()]) ];")));

    // A chain that ends at a STEP rather than at the children indexer is typed Build<T> — a struct,
    // which inherits from nothing, so the Component-derived check saw nothing and the key check never
    // ran. The shape was unreachable until the children indexer gained its `params object?[]`
    // overload; now that a projection of chains can BE children, the blindness is a live false
    // negative and this is the case that pins it.
    [Fact]
    public async Task A_chain_ending_at_a_step_with_no_key_reports_RASK022()
    {
        var d = Assert.Single(await Diagnostics(App(
            "return Ul[ _items.Select(i => Li.Class(\"row\")) ];")));
        Assert.Equal("RASK022", d.Id);
        Assert.Contains("Li", d.GetMessage());
    }

    [Fact]
    public async Task A_chain_ending_at_a_step_with_a_key_reports_nothing() =>
        Assert.Empty(await Diagnostics(App(
            "return Ul[ _items.Select(i => Li.Key(i).Class(\"row\")) ];")));

    [Fact]
    public async Task A_Select_projection_with_no_key_reports_RASK022()
    {
        var d = Assert.Single(await Diagnostics(App(
            "return Ul[ _items.Select(i => Li[i.ToString()]) ];")));
        Assert.Equal("RASK022", d.Id);
        Assert.Contains("Li", d.GetMessage());
    }

    [Fact]
    public async Task A_foreach_adding_to_a_child_list_with_no_key_reports_RASK022()
    {
        var d = Assert.Single(await Diagnostics(App("""
                                                    var rows = new List<Component>();
                                                    foreach (var i in _items) rows.Add(Tr[i.ToString()]);
                                                    return Ul[rows];
                                                    """)));
        Assert.Equal("RASK022", d.Id);
        Assert.Contains("Tr", d.GetMessage());
    }

    [Fact]
    public async Task A_Select_projection_with_a_key_reports_nothing()
    {
        Assert.Empty(await Diagnostics(App(
            "return Ul[ _items.Select(i => Li.Key(i)[i.ToString()]) ];")));
    }

    [Fact]
    public async Task A_Select_projection_with_a_data_rask_key_reports_nothing()
    {
        Assert.Empty(await Diagnostics(App("""
                                           return Ul[ _items.Select(i => Li(
                                               Data: new Dictionary<string, string?> { ["rask-key"] = i.ToString() })[i.ToString()]) ];
                                           """)));
    }

    [Fact]
    public async Task A_foreach_adding_to_a_child_list_with_a_key_reports_nothing()
    {
        Assert.Empty(await Diagnostics(App("""
                                           var rows = new List<Component>();
                                           foreach (var i in _items) rows.Add(Tr.Key(i)[i.ToString()]);
                                           return Ul[rows];
                                           """)));
    }

    [Fact]
    public async Task Only_the_outer_projected_item_is_flagged_not_its_nested_child()
    {
        // Li is the projected list item (flagged once); the nested Code is Li's child, not a
        // sibling in the reconciled list, so it must NOT be flagged.
        var d = Assert.Single(await Diagnostics(App(
            "return Ul[ _items.Select(i => Li[ Code[i.ToString()] ]) ];")));
        Assert.Contains("Li", d.GetMessage());
    }

    [Fact]
    public async Task A_single_static_child_is_not_a_list_and_reports_nothing() =>
        Assert.Empty(await Diagnostics(App("return Div()[ Span()[\"hi\"] ];")));

    [Fact]
    public async Task An_Add_outside_a_loop_reports_nothing()
    {
        // A one-off Add (not in a loop) isn't a reconciled list — don't warn.
        Assert.Empty(await Diagnostics(App("""
                                           var rows = new List<Component>();
                                           rows.Add(Tr["one"]);
                                           return Ul[rows];
                                           """)));
    }

    // A GENERIC component cannot hand back `Build<T>` from its entry — its own type argument is not
    // known until a step pins it — so the entry is typed `RaskSeed_<Name>` and the first step returns
    // the chain. Matching only `Build<T>` therefore stood this check down on every generic component and
    // every form control, silently: the analyzer did not report anything wrong, it simply never ran.
    [Fact]
    public async Task A_seed_opened_chain_in_a_projection_with_no_key_reports_RASK022()
    {
        var d = Assert.Single(await Diagnostics(Seeded(
            "return Ul[ _items.Select(i => Row.Item(i)) ];")));

        Assert.Equal("RASK022", d.Id);
    }

    [Fact]
    public async Task A_seed_opened_chain_in_a_projection_with_a_key_reports_nothing() =>
        Assert.Empty(await Diagnostics(Seeded(
            "return Ul[ _items.Select(i => Row.Item(i).Key(i.ToString())) ];")));

    // Wraps a Render() body alongside a generic component, whose entry is a seed rather than a chain.
    private static string Seeded(string body) => $$"""
        using System.Collections.Generic;
        using System.Linq;
        using Rask.Core;
        namespace Demo;

        public sealed partial class Row<T> : Component
        {
            public required T Item { get; set; }
            protected override Component? Render() => null;
        }

        public sealed partial class App : Component
        {
            private readonly int[] _items = { 1, 2, 3 };
            protected override Component? Render()
            {
                {{body}}
            }
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // The chain needs the builder entries, and a referenced library's component only has them once the
        // generator has injected them — inheritance no longer supplies it.
        compilation = (CSharpCompilation)GeneratorDriverFixture.WithBuilderSurface(compilation);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MissingKeyAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK022").ToImmutableArray();
    }
}
