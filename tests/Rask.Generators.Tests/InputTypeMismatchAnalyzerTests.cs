using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class InputTypeMismatchAnalyzerTests
{
    // Wraps a Render() body. The real Rask.Html Input<T> entry + InputType are referenced via
    // BuildReferences(), so the analyzer resolves the genuine control and its value type T.
    private static string App(string body) => $$"""
                                                using System;
                                                using Rask.Core;
                                                namespace Demo;
                                                public sealed partial class App : Component
                                                {
                                                    private sealed class M
                                                    {
                                                        public int Age { get; set; }
                                                        public string Name { get; set; } = "";
                                                        public bool Flag { get; set; }
                                                    }
                                                    private readonly M _m = new();
                                                    protected override Component? Render()
                                                    {
                                                        {{body}}
                                                    }
                                                }
                                                """;

    // Named in full because `App` is not a markup host in this fixture — it declares a component, so it is
    // given no entries of its own and the bare `Input` would be the TYPE (RASK043 says exactly that).
    private const string Entry = "global::RaskEntriesRask_Html.Input";

    [Fact]
    public async Task StringFamilyType_OnBoolInput_ReportsRask025() =>
        Assert.Equal("RASK025",
            Assert.Single(await Diagnostics(App($"return {Entry}.Bind(() => _m.Flag).Type(InputType.Text);"))).Id);

    [Fact]
    public async Task StringFamilyType_OnIntInput_ReportsRask025()
    {
        var d = Assert.Single(await Diagnostics(App("return global::RaskEntriesRask_Html.Input.Bind(() => _m.Age).Type(InputType.Email);")));
        Assert.Equal("RASK025", d.Id);
        Assert.Contains("Email", d.GetMessage());
    }

    [Fact]
    public async Task StringFamilyType_OnStringInput_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Input.Bind(() => _m.Name).Type(InputType.Email);")));

    [Fact]
    public async Task NumberType_OnIntInput_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Input.Bind(() => _m.Age).Type(InputType.Number);")));

    [Fact]
    public async Task NoExplicitType_OnIntInput_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Input.Bind(() => _m.Age);")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new InputTypeMismatchAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK025").ToImmutableArray();
    }
}
