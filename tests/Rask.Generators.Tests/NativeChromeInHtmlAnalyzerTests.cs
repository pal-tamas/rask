using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class NativeChromeInHtmlAnalyzerTests
{
    [Fact]
    public async Task NativeComponentAsElementChild_ReportsRask032()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() => Div[NativeHeaderBar];
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
                  using Rask.Native.Components;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() => Div[default(Build<NativeHeaderBar>)];
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
        // a bar inside any other element (the NativeWebView[...] children indexer is what's flagged).
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() => NativeWebView[NativeHeaderBar];
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
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() =>
                      [
                          NativeHeaderBar(),
                          NativeWebView[Div[Span]],
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
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() => Div[Span];
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
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() => Div[NativeHeaderBar];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src, "Rask.Native");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NativeComponentAsElementChild_InChainSyntax_ReportsRask032()
    {
        // The chain yields Build<T>, not T. Until the analyzer saw through it, a chain RECEIVER was an
        // unrecognized type and the rule went quiet — green on the syntax the docs actually teach.
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() => Div[NativeHeaderBar];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK032", d.Id);
        Assert.Contains("NativeHeaderBar", d.GetMessage());
    }

    [Fact]
    public async Task HtmlInsideNativeScreen_ReportsRask048()
    {
        // A pure-native screen has no WebView behind it, so a Div there renders nothing at all.
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() => NativeScreen[Div];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src, id: "RASK048");

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK048", d.Id);
        Assert.Contains("Div", d.GetMessage());
    }

    [Fact]
    public async Task HtmlInsideANativeStack_ReportsRask048()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() => NativeScreen[NativeStack[Span]];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src, id: "RASK048");

        Assert.Equal("RASK048", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task NativeViewsInsideANativeScreen_NoDiagnostic()
    {
        // The whole point of the pure-native family: native views compose inside a screen. This must NOT be
        // mistaken for "a native component in the HTML tree".
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() =>
                          NativeScreen[NativeStack[NativeLabel["hi"], NativeButton["go"]]];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src, id: null);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task MixedApp_WebViewAndScreenOnDifferentBranches_NoDiagnostic()
    {
        // One app, two surfaces — markup inside the WebView, native views inside the screen.
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      public bool Native { get; set; }
                      protected override Component? Render() =>
                          Native ? NativeScreen[NativeLabel["hi"]] : NativeWebView[Div[Span]];
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src, id: null);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source,
        string assemblyName = "TestAssembly", string? id = "RASK032")
    {
        // Analyze the post-generator compilation with the builder surface ON, so the chain syntax
        // (`Div[NativeHeaderBar]`) binds to real generated entries. Analyzing the bare source would leave
        // every chain expression an error type, and a chain test would report nothing and pass for the wrong
        // reason.
        var generated = BuilderGeneratorHarness.Compile(source, assemblyName);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new NativeChromeInHtmlAnalyzer());
        var withAnalyzers = generated.WithAnalyzers(analyzers);
        var all = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        // id: null asks for "nothing at all should be reported", which is a stronger assertion than filtering
        // to one rule and finding it empty — that would pass while the OTHER rule false-positives.
        return id is null ? all : all.Where(d => d.Id == id).ToImmutableArray();
    }
}
