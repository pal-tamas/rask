using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class NativeChromeInHtmlAnalyzerTests
{
    [Fact]
    public async Task NativeComponentAsElementChild_ReportsRask032()
    {
        var src = """
                  using Rask.Core;
                  using static Rask.Core.Components.Generated;
                  using static Rask.Native.Components.Generated;
                  namespace Demo;
                  public sealed class Page : Component
                  {
                      protected override Component? Render() => Div()[NativeHeaderBar()];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK032", d.Id);
        Assert.Contains("NativeHeaderBar", d.GetMessage());
    }

    // The chain is what the framework teaches, and every link of one — the bare entry included — is typed
    // Build<T> rather than T. The type test walked straight past that, so native chrome nested in HTML
    // went unreported across the whole chain surface.
    //
    // Spelled with an explicit Build<NativeHeaderBar> rather than `NativeHeaderBar.Title("Hi")` because a
    // native component's ENTRY is injected into the consumer by the generator, which does not run in this
    // harness — written that way the snippet does not bind at all (CS0103), and the test would fail for a
    // reason that has nothing to do with the analyzer. The type handed to the indexer is identical.
    [Fact]
    public async Task ChainNativeComponentAsElementChild_ReportsRask032()
    {
        var src = """
                  using Rask.Core;
                  using static Rask.Core.Components.Generated;
                  using Rask.Native.Components;
                  namespace Demo;
                  public sealed class Page : Component
                  {
                      protected override Component? Render() => Div()[default(Build<NativeHeaderBar>)];
                  }
                  """;

        var d = Assert.Single(await GetDiagnosticsAsync(src));
        Assert.Equal("RASK032", d.Id);
        Assert.Contains("NativeHeaderBar", d.GetMessage());
    }

    [Fact]
    public async Task NativeComponentInsideNativeWebViewContent_ReportsRask032()
    {
        // The HTML that NativeWebView hosts must not contain native chrome — a bar there is the same mistake as
        // a bar inside any other element (the NativeWebView()[...] children indexer is what's flagged).
        var src = """
                  using Rask.Core;
                  using static Rask.Core.Components.Generated;
                  using static Rask.Native.Components.Generated;
                  namespace Demo;
                  public sealed class Page : Component
                  {
                      protected override Component? Render() => NativeWebView()[NativeHeaderBar()];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK032", d.Id);
        Assert.Contains("NativeHeaderBar", d.GetMessage());
    }

    [Fact]
    public async Task NativeComponentsComposedAtLayoutLevel_NoDiagnostic()
    {
        // The supported usage: bars composed as siblings of NativeWebView in Render(). A collection is not an
        // element-children indexer, so returning native chrome from Render() is legal.
        var src = """
                  using Rask.Core;
                  using static Rask.Core.Components.Generated;
                  using static Rask.Native.Components.Generated;
                  namespace Demo;
                  public sealed class Page : Component
                  {
                      protected override Component? Render() =>
                      [
                          NativeHeaderBar(),
                          NativeWebView()[Div()[Span()]],
                          NativeTabBar()
                      ];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task PlainHtmlRender_NoDiagnostic()
    {
        var src = """
                  using Rask.Core;
                  using static Rask.Core.Components.Generated;
                  namespace Demo;
                  public sealed class Page : Component
                  {
                      protected override Component? Render() => Div()[Span()];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NativeComponentInRaskNativeAssembly_NoDiagnostic()
    {
        // The Rask.Native assembly itself legitimately references these types outside an HTML context — the same
        // element-child placement that flags in a consumer is skipped there.
        var src = """
                  using Rask.Core;
                  using static Rask.Core.Components.Generated;
                  using static Rask.Native.Components.Generated;
                  namespace Demo;
                  public sealed class Page : Component
                  {
                      protected override Component? Render() => Div()[NativeHeaderBar()];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src, "Rask.Native");

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source,
        string assemblyName = "TestAssembly")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = GeneratorDriverFixture.BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new NativeChromeInHtmlAnalyzer());
        var withAnalyzers = compilation.WithAnalyzers(analyzers);
        var all = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK032").ToImmutableArray();
    }
}
