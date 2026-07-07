using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class PreferNamedArgsAnalyzerTests
{
    private static string App(string body) => $$"""
        using Rask.Core;
        using static Rask.Core.Components.Generated;
        namespace Demo;
        public sealed class App : Component
        {
            protected override Component? Render()
            {
                {{body}}
            }
        }
        """;

    [Fact]
    public async Task ThreePositionalArgs_ReportsRask030()
    {
        // Div(Id, Class, Style, …) — three leading positional strings: past the readable/idiomatic limit.
        var d = Assert.Single(await Diagnostics(App("return Div(\"the-id\", \"the-class\", \"color:red\");")));
        Assert.Equal("RASK030", d.Id);
        Assert.Contains("3 positional", d.GetMessage());
    }

    [Fact]
    public async Task TwoPositionalArgs_NoDiagnostic() =>
        // One or two positional args (primary content + a class) stay idiomatic.
        Assert.Empty(await Diagnostics(App("return Div(\"the-id\", \"the-class\");")));

    [Fact]
    public async Task SinglePositionalArg_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return A(\"/home\");")));

    [Fact]
    public async Task NamedArgs_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Div(Id: \"the-id\", Class: \"the-class\", Style: \"color:red\");")));

    [Fact]
    public async Task TwoPositionalThenNamed_NoDiagnostic() =>
        // Only two positional arguments; the rest are explicit already.
        Assert.Empty(await Diagnostics(App("return Div(\"the-id\", \"the-class\", Style: \"color:red\");")));

    [Fact]
    public async Task NonRaskCall_NoDiagnostic() =>
        // string.Concat is not a Rask factory (three positional args, but not a Component factory).
        Assert.Empty(await Diagnostics(App("_ = string.Concat(\"a\", \"b\", \"c\"); return null;")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new PreferNamedArgsAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK030").ToImmutableArray();
    }
}
