using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class InputTypeMismatchAnalyzerTests
{
    // Wraps a Render() body. Real Rask.Core factories (Generated.Input<T>) + InputType are referenced via
    // BuildReferences(), so the analyzer resolves the genuine generic Input factory and value type T.
    private static string App(string body) => $$"""
                                                using System;
                                                using Rask.Core;
                                                using static Rask.Core.Components.Generated;
                                                namespace Demo;
                                                public sealed class App : Component
                                                {
                                                    private sealed class M
                                                    {
                                                        public int Age { get; set; }
                                                        public string Name { get; set; } = "";
                                                        public bool Flag { get; set; }
                                                    }
                                                    private readonly M _m = new();
                                                    protected override RenderResult Render()
                                                    {
                                                        {{body}}
                                                    }
                                                }
                                                """;

    [Fact]
    public async Task StringFamilyType_OnIntInput_ReportsRask025()
    {
        var d = Assert.Single(await Diagnostics(App("return Input(() => _m.Age, Type: InputType.Email);")));
        Assert.Equal("RASK025", d.Id);
        Assert.Contains("Email", d.GetMessage());
    }

    [Fact]
    public async Task StringFamilyType_OnBoolInput_ReportsRask025() =>
        Assert.Equal("RASK025",
            Assert.Single(await Diagnostics(App("return Input(() => _m.Flag, Type: InputType.Text);"))).Id);

    [Fact]
    public async Task StringFamilyType_OnStringInput_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Input(() => _m.Name, Type: InputType.Email);")));

    [Fact]
    public async Task NoExplicitType_OnIntInput_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Input(() => _m.Age);")));

    [Fact]
    public async Task NumberType_OnIntInput_NoDiagnostic() =>
        // Number is not a string-family type — pairing it with Input<int> is fine.
        Assert.Empty(await Diagnostics(App("return Input(() => _m.Age, Type: InputType.Number);")));

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
