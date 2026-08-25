using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     <c>NativeWebView</c> shows one document: the page at its <c>Url</c>, or the children composed into it.
///     RASK049 catches one NativeWebView claiming both; RASK050 catches one component that could render
///     either.
/// </summary>
public class NativeWebViewModeAnalyzerTests
{
    [Fact]
    public async Task UrlAndChildrenOnOneWebView_ReportsRask049()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() =>
                          NativeWebView.Url("https://app.example.com/")[Div[Span]];
                  }
                  """;

        var d = Assert.Single(await GetDiagnosticsAsync(src, id: "RASK049"));
        Assert.Equal("RASK049", d.Id);
        Assert.Contains("one document", d.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>The typed step is the same mistake and must not slip past the syntactic name check.</summary>
    [Fact]
    public async Task UrlAsUriAndChildren_ReportsRask049()
    {
        var src = """
                  using System;
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() =>
                          NativeWebView.Url(new Uri("https://app.example.com/"))[Div];
                  }
                  """;

        Assert.Single(await GetDiagnosticsAsync(src, id: "RASK049"));
    }

    /// <summary>A step after the Url must not hide it — the walk goes up the whole chain, not one link.</summary>
    [Fact]
    public async Task UrlFollowedByAnotherStepThenChildren_ReportsRask049()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() =>
                          NativeWebView.Url("https://app.example.com/").Key("k")[Div];
                  }
                  """;

        Assert.Single(await GetDiagnosticsAsync(src, id: "RASK049"));
    }

    [Fact]
    public async Task MarkupModeAlone_ReportsNothing()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() => NativeWebView[Div[Span]];
                  }
                  """;

        Assert.Empty(await GetDiagnosticsAsync(src, id: null));
    }

    [Fact]
    public async Task UrlModeAlone_ReportsNothing()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      protected override Component? Render() =>
                          [NativeHeaderBar.Title("Remote"), NativeWebView.Url("https://app.example.com/")];
                  }
                  """;

        Assert.Empty(await GetDiagnosticsAsync(src, id: null));
    }

    [Fact]
    public async Task BothModesInOneComponent_ReportsRask050()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      public bool Remote { get; set; }
                      protected override Component? Render() =>
                          Remote ? NativeWebView.Url("https://app.example.com/") : NativeWebView[Div[Span]];
                  }
                  """;

        var d = Assert.Single(await GetDiagnosticsAsync(src, id: "RASK050"));
        Assert.Equal("RASK050", d.Id);
        // It reports on the MARKUP arm — the Url is the deliberate choice, the markup is what silently
        // stopped working — and names the component that holds both.
        Assert.Contains("pick a mode", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Page", d.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A compilation is NOT an app. A test project, a component library, or any assembly with more than
    ///     one app root legitimately holds both modes in different types — a compilation-wide rule reported
    ///     every one of them, which is how this scope was found.
    /// </summary>
    [Fact]
    public async Task TheTwoModesInSeparateComponents_ReportsNothing()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Remote : Component
                  {
                      protected override Component? Render() => NativeWebView.Url("https://app.example.com/");
                  }
                  public sealed partial class Local : Component
                  {
                      protected override Component? Render() => NativeWebView[Div[Span]];
                  }
                  """;

        Assert.Empty(await GetDiagnosticsAsync(src, id: null));
    }

    /// <summary>
    ///     A pure-native screen is not a third mode and must not be dragged in: it paints through the surface,
    ///     not the WebView, and composing one beside either mode is the existing mixed-surface feature.
    /// </summary>
    [Fact]
    public async Task ANativeScreenBesideUrlMode_ReportsNothing()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Page : Component
                  {
                      public bool Native { get; set; }
                      protected override Component? Render() =>
                          Native ? NativeScreen[NativeLabel["hi"]] : NativeWebView.Url("https://app.example.com/");
                  }
                  """;

        Assert.Empty(await GetDiagnosticsAsync(src, id: null));
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source, string assemblyName = "TestAssembly", string? id = "RASK049")
    {
        // Analyze the POST-generator compilation, so `NativeWebView.Url(…)` binds to the real generated step
        // rather than sitting there as an error type — a chain test against bare source reports nothing and
        // passes for the wrong reason.
        var generated = BuilderGeneratorHarness.Compile(source, assemblyName);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new NativeWebViewModeAnalyzer());
        var all = await generated.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        // id: null asks for "nothing at all", which is stronger than filtering to one rule and finding it
        // empty — that would pass while the other rule false-positives.
        return id is null ? all : all.Where(d => d.Id == id).ToImmutableArray();
    }
}
