using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

public class ComponentScopedCssGeneratorTests
{
    [Fact]
    public void Generator_EmitsRegistrationForMatchingComponentAndCssSibling()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.css", ".counter { color: red; }") });

        var generated = run.GeneratedSource("__RaskScopedCssRegistration");
        Assert.Contains("typeof(global::Foo.Counter)", generated);
        Assert.Contains(".counter { color: red; }", generated);
        Assert.Contains("RegisterCss", generated);
        Assert.Contains("ModuleInitializer", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_DoesNotPairCssWithComponentInDifferentDirectory()
    {
        // Counter.css under /proj/Pages/ should not bind to /proj/Other/Counter (same simple
        // name, different directory) — they're orphan from each other's perspective.
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Other/Counter.cs", source) },
            new[] { ("/proj/Pages/Counter.css", ".counter { color: red; }") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK015");
    }

    [Fact]
    public void Generator_RaisesRASK015_ForOrphanCssFile()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Conter.css", ".typo { color: red; }") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK015");
    }

    [Fact]
    public void Generator_EscapesQuotesInCssContent()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.css", ".x::before { content: \"a\"; }") });

        var generated = run.GeneratedSource("__RaskScopedCssRegistration");
        // verbatim string literal escapes " as ""
        Assert.Contains("\"\"a\"\"", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_IgnoresWhitespaceOnlyCssFile()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.css", "   \n\t  ") });

        // Whitespace-only CSS is silently ignored — neither a registration nor an orphan
        // diagnostic. The file exists but contributes nothing.
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var anyRegistration = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Any(s => s.HintName.Contains("__RaskScopedCssRegistration"));
        Assert.False(anyRegistration);
    }

    [Fact]
    public void Generator_SkipsAbstractComponents()
    {
        // An abstract Component in the same directory as a .css file shouldn't match —
        // abstract classes can't be instantiated and shouldn't host scoped styles. The
        // pairing rule excludes them; the .css file becomes an orphan (RASK015).
        const string source = """
                              namespace Foo;
                              public abstract class Base : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Base.cs", source) },
            new[] { ("/proj/Base.css", ".x { color: red; }") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK015");
    }

    private static GeneratorRun Run(
        (string Path, string Source)[] sources,
        (string Path, string Contents)[] additionalCss) =>
        GeneratorDriverFixture.Run(sources, new ComponentScopedCssGenerator(), additionalCss);
}
