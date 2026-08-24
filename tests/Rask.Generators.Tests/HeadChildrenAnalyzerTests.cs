using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

// RASK019 — `<head>` is a framework-managed slot, so passing children to Head is a mistake: they are
// dropped. The analyzer had no tests of its own; these cover the factory spelling it was written for and
// the chain spelling that is what the framework now teaches.
public class HeadChildrenAnalyzerTests
{
    private static string App(string body) => $$"""
        using Rask.Core;
        namespace Demo;
        public sealed partial class Page : Component
        {
            protected override Component? Render()
            {
                {{body}}
            }
        }
        """;

    // The chain receiver is Build<Head>, not Head, so the type test walked straight past it (#704).
    [Fact]
    public async Task ChainHeadWithChildren_ReportsRask019() =>
        Assert.Equal("RASK019", Assert.Single(await Diagnostics(App("return Head[Title[\"x\"]];"))).Id);

    [Fact]
    public async Task HeadWithNoChildren_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Div[Head];")));

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

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new HeadChildrenAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK019").ToImmutableArray();
    }
}
