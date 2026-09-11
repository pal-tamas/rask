using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

// RASK062 — children an island cannot render. A Blazor island takes none. A JS island takes children of its own
// runtime through the typed indexers the generator writes; Rask markup or another runtime's island binds one of the
// hiding indexers ExternalComponent declares, which the compiler rejects only where the result is used (or never, for
// a `var`), so the analyzer reports the binding at the brackets.
//
// The island bases are declared in the snippet rather than referenced. Rask.Generators.Tests references neither
// package, and the analyzer resolves them by metadata name — so source-declared types in those namespaces exercise
// exactly the lookup the real ones go through. The stubs mirror the real shapes: ExternalComponent's `new` indexers
// return the ref struct, and an island that takes content declares its own typed indexer.
public class IslandChildrenAnalyzerTests
{
    private static string App(string body) => $$"""
        using System.Collections.Generic;
        using Rask.Core;

        namespace Rask.Blazor
        {
            public abstract class BlazorComponent<T> : Component where T : new() { }
        }

        namespace Rask.External
        {
            public readonly ref struct NotAChildOfThisIsland { }

            public abstract class ExternalComponent : Component
            {
                public new NotAChildOfThisIsland this[params Component?[] children] => default;
                public new NotAChildOfThisIsland this[IEnumerable<Component?> children] => default;
                public new NotAChildOfThisIsland this[params object?[] children] => default;
            }

            public abstract class ReactComponent : ExternalComponent { }
            public abstract class VueComponent : ExternalComponent { }

            public readonly struct ReactChild
            {
                public static implicit operator ReactChild(string? text) => default;
                public static implicit operator ReactChild(int value) => default;
                public static implicit operator ReactChild(ReactComponent? island) => default;
            }
        }

        namespace Demo
        {
            using Rask.Blazor;
            using Rask.External;

            public sealed class Hosted { }
            public sealed class Chart : BlazorComponent<Hosted> { }
            public sealed class Plain : Component { }

            // A React island whose component takes content: the generator writes its typed indexers.
            public sealed class Card : ReactComponent
            {
                public Card this[params ReactChild[] children] => this;
                public Card this[IEnumerable<ReactChild> children] => this;
            }

            // A React island whose component takes nothing: no typed indexer, only the hiding ones.
            public sealed class Badge : ReactComponent { }

            public sealed class Toggle : VueComponent { }

            public sealed class Page : Component
            {
                protected override Component? Render()
                {
                    Component leaf = new Plain();
                    {{body}}
                }
            }
        }
        """;

    [Fact]
    public async Task ChainBlazorIslandWithChildren_ReportsRask062() =>
        Assert.Equal("RASK062", Assert.Single(await Diagnostics(App("return default(Chart)[leaf];"))).Id);

    [Fact]
    public async Task BlazorIslandWithChildren_ReportsRask062() =>
        Assert.Equal("RASK062", Assert.Single(await Diagnostics(App("Chart c = new(); return c[leaf];"))).Id);

    // The message names the island, so the error points at the type the author wrote.
    [Fact]
    public async Task TheMessageNamesTheIsland() =>
        Assert.StartsWith("'Chart' is a Blazor island", Assert.Single(
            await Diagnostics(App("Chart c = new(); return c[leaf];"))).GetMessage(), StringComparison.Ordinal);

    // Rask markup inside a React island binds a hiding indexer. `var` is the spelling the compiler lets through
    // silently — a ref struct local is legal — so it is the one that proves the analyzer, not CS0029, reports it.
    [Fact]
    public async Task RaskMarkupInsideAJsIsland_ReportsRask062_NamingTheRuntime()
    {
        var diagnostic = Assert.Single(await Diagnostics(App("var bad = default(Card)[leaf]; return leaf;")));

        Assert.Equal(
            "'Card' is a React island; its children are React islands, text, numbers and dates",
            diagnostic.GetMessage());
    }

    [Fact]
    public async Task AnotherRuntimesIslandInsideAJsIsland_ReportsRask062() =>
        Assert.Equal("RASK062", Assert.Single(
            await Diagnostics(App("var bad = default(Card)[default(Toggle)]; return leaf;"))).Id);

    // An island whose component takes no content has no typed indexer, so even text is refused — with a message that
    // says so rather than listing children it would also refuse.
    [Fact]
    public async Task AnyChildOfAnIslandThatTakesNone_SaysItTakesNoChildren() =>
        Assert.Equal(
            "'Badge' is a React island that takes no children; place the markup around it instead of inside it",
            Assert.Single(await Diagnostics(App("var bad = default(Badge)[\"label\"]; return leaf;"))).GetMessage());

    [Fact]
    public async Task ChildrenAssignedToAnIsland_ReportsRask062()
    {
        var diagnostics = await Diagnostics(App("Card card = default!; card.Children = [leaf]; Chart chart = new(); chart.Children = [leaf]; return card;"));

        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, d => d.GetMessage().StartsWith("'Card' is a React island and never renders Children", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.GetMessage().StartsWith("'Chart' is a Blazor island", StringComparison.Ordinal));
    }

    // Controls. Children of the island's own runtime are its whole composition model — without these a broken analyzer
    // that reported every element access on an island would still pass every test above.
    [Fact]
    public async Task SameRuntimeChildrenOfAJsIsland_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return default(Card)[\"Revenue\", 42, default(Badge), default(Card)[\"nested\"]];")));

    [Fact]
    public async Task IslandWithNoChildren_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("Chart c = new(); return c;")));

    [Fact]
    public async Task OrdinaryComponentWithChildren_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("Plain p = new(); p.Children = [leaf]; return p[leaf];")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // A snippet that does not bind reports nothing, which would read as "the analyzer is fine".
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0, "The snippet under test does not compile:\n"
                                       + string.Join("\n", errors.Select(e => e.ToString())));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new IslandChildrenAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK062").ToImmutableArray();
    }
}
