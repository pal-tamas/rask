using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class ImgMissingAltAnalyzerTests
{
    // Wraps a Render() body in a component. Real Rask.Core factories (Generated.Img/...) are
    // referenced via BuildReferences(), so the analyzer resolves the genuine Img factory symbol.
    private static string App(string body) => $$"""
                                                using System.Collections.Generic;
                                                using Rask.Core;
                                                using static Rask.Core.Components.Generated;
                                                namespace Demo;
                                                public sealed class App : Component
                                                {
                                                    protected override RenderResult Render()
                                                    {
                                                        {{body}}
                                                    }
                                                }
                                                """;

    [Fact]
    public async Task Img_NoAlt_ReportsRask023()
    {
        var d = Assert.Single(await Diagnostics(App("return Img(Src: \"/a.png\");")));
        Assert.Equal("RASK023", d.Id);
        Assert.Contains("Alt", d.GetMessage());
    }

    [Fact]
    public async Task Img_NoArguments_ReportsRask023() =>
        Assert.Equal("RASK023", Assert.Single(await Diagnostics(App("return Img();"))).Id);

    [Fact]
    public async Task Img_AltByName_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Img(Src: \"/a.png\", Alt: \"A logo\");")));

    [Fact]
    public async Task Img_EmptyAltForDecorative_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Img(Src: \"/a.png\", Alt: \"\");")));

    [Fact]
    public async Task Img_AltPositionally_NoDiagnostic() =>
        // Factory order is Src, Alt, ... so the second positional argument is Alt.
        Assert.Empty(await Diagnostics(App("return Img(\"/a.png\", \"A logo\");")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ImgMissingAltAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK023").ToImmutableArray();
    }
}
