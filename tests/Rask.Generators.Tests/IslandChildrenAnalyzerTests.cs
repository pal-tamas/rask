using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

// RASK062 — an island renders markup a foreign renderer owns and takes no Rask children. The children
// indexer lives on Component and Build<T>, so it cannot be withheld from one type: without the analyzer
// the children bind, compile, and silently never render.
//
// Both island bases are declared in the snippet rather than referenced. Rask.Generators.Tests references
// neither package, and the analyzer resolves them by metadata name — so source-declared types in those
// namespaces exercise exactly the lookup the real ones go through.
public class IslandChildrenAnalyzerTests
{
    private static string App(string body) => $$"""
        using Rask.Core;

        namespace Rask.Blazor
        {
            public abstract class BlazorComponent<T> : Component where T : new() { }
        }

        namespace Rask.External
        {
            public abstract class ExternalComponent : Component { }
        }

        namespace Demo
        {
            using Rask.Blazor;
            using Rask.External;

            public sealed class Hosted { }
            public sealed class Chart : BlazorComponent<Hosted> { }
            public sealed class Panel : ExternalComponent { }
            public sealed class Plain : Component { }

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

    // The spelling that matters: the chain's receiver is Build<Chart>, never Chart. An analyzer that
    // compares the receiver type directly walks straight past every chain — the documented way seven
    // of this repo's analyzers were blind.
    [Fact]
    public async Task ChainBlazorIslandWithChildren_ReportsRask062() =>
        Assert.Equal("RASK062", Assert.Single(await Diagnostics(App("return default(Build<Chart>)[leaf];"))).Id);

    [Fact]
    public async Task ChainExternalIslandWithChildren_ReportsRask062() =>
        Assert.Equal("RASK062", Assert.Single(await Diagnostics(App("return default(Build<Panel>)[leaf];"))).Id);

    [Fact]
    public async Task BlazorIslandWithChildren_ReportsRask062() =>
        Assert.Equal("RASK062", Assert.Single(await Diagnostics(App("Chart c = new(); return c[leaf];"))).Id);

    [Fact]
    public async Task ExternalIslandWithChildren_ReportsRask062() =>
        Assert.Equal("RASK062", Assert.Single(await Diagnostics(App("Panel p = new(); return p[leaf];"))).Id);

    // The message names the island, so the error points at the type the author wrote.
    [Fact]
    public async Task TheMessageNamesTheIsland() =>
        Assert.Contains("'Chart'", Assert.Single(
            await Diagnostics(App("Chart c = new(); return c[leaf];"))).GetMessage(), StringComparison.Ordinal);

    // Control: an island is perfectly legal, it just takes no children. Without this a broken analyzer
    // that reported on every element access would still pass every test above.
    [Fact]
    public async Task IslandWithNoChildren_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("Chart c = new(); return c;")));

    // Control: children on an ordinary component are the framework's whole composition model.
    [Fact]
    public async Task OrdinaryComponentWithChildren_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("Plain p = new(); return p[leaf];")));

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
