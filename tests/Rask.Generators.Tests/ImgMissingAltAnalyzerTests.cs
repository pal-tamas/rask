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
                                                namespace Demo;
                                                public sealed partial class App : Component
                                                {
                                                    protected override Component? Render()
                                                    {
                                                        {{body}}
                                                    }
                                                }
                                                """;


    [Fact]
    public async Task An_Img_given_its_alt_positionally_reports_nothing() =>
        // Factory order is Src, Alt, ... so the second positional argument is Alt.
        Assert.Empty(await Diagnostics(App("return Img(\"/a.png\", \"A logo\");")));

    // The chain is what the framework teaches now, so the a11y guard has to see it. These are the same
    // four cases as above, written the way a user writes them today.
    [Fact]
    public async Task A_chain_with_no_alt_reports_RASK023()
    {
        var d = Assert.Single(await Diagnostics(App("return Img.Src(\"/a.png\");")));
        Assert.Equal("RASK023", d.Id);
        Assert.Contains("Alt", d.GetMessage());
    }

    [Fact]
    public async Task A_bare_entry_with_no_alt_reports_RASK023() =>
        // The shortest spelling of all: no invocation anywhere, just the entry.
        Assert.Equal("RASK023", Assert.Single(await Diagnostics(App("return Img;"))).Id);

    [Fact]
    public async Task A_chain_with_an_alt_reports_nothing() =>
        Assert.Empty(await Diagnostics(App("return Img.Src(\"/a.png\").Alt(\"A logo\");")));

    [Fact]
    public async Task A_chain_with_an_empty_alt_for_a_decorative_image_reports_nothing() =>
        Assert.Empty(await Diagnostics(App("return Img.Alt(\"\").Src(\"/a.png\");")));

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

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ImgMissingAltAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK023").ToImmutableArray();
    }
}
