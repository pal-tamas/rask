using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

// RASK117 — Cached stores static markup, so a handler, a binding or a reference written into its children would be
// served to the next visitor dead.
public class CachedInteractiveAnalyzerTests
{
    private static string App(string body) => $$"""
        using Rask.Core;
        namespace Demo;
        public sealed partial class Page : Component
        {
            private string _name = "";
            private void Save() { }
            protected override Component? Render()
            {
                {{body}}
            }
        }
        """;

    [Fact]
    public async Task A_click_handler_inside_a_cached_fragment_reports_RASK117()
    {
        var diagnostics = await Diagnostics(App("""return Cached.Name("cart")[Div[Button.OnClick(Save)["Add"]]];"""));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'OnClick' inside Cached", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_binding_inside_a_cached_fragment_reports_RASK117() =>
        Assert.Single(await Diagnostics(App("""return Cached.Name("form")[Input.Bind(() => _name)];""")));

    [Fact]
    public async Task Static_markup_inside_and_a_handler_beside_the_fragment_report_nothing() =>
        Assert.Empty(await Diagnostics(App("""return Div[Cached.Name("list").For(System.TimeSpan.FromMinutes(5))[Ul[Li["a"]]], Button.OnClick(Save)["Go"]];""")));

    [Fact]
    public async Task A_handler_in_a_nested_fragment_is_reported_once() =>
        Assert.Single(await Diagnostics(App("""return Cached.Name("outer")[Cached.Name("inner")[Button.OnClick(Save)["Go"]]];""")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // The chain's steps are generated into the consuming assembly, so run the generator first.
        compilation = (CSharpCompilation)GeneratorDriverFixture.WithBuilderSurface(compilation);

        // A snippet that does not bind reports nothing, which would read as "the analyzer is fine".
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0, "The snippet under test does not compile:\n"
                                       + string.Join("\n", errors.Select(e => e.ToString())));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new CachedInteractiveAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK117").ToImmutableArray();
    }
}
