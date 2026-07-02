using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

public class ComponentScopedJsGeneratorTests
{
    [Fact]
    public void Generator_EmitsRegistrationForMatchingComponentAndJsSibling()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.js", "export function rendered(el) { el.dataset.rendered = '1'; }") });

        var generated = run.GeneratedSource("__RaskScopedJsRegistration");
        Assert.Contains("typeof(global::Foo.Counter)", generated);
        Assert.Contains("el.dataset.rendered", generated);
        Assert.Contains("RegisterJs", generated);
        Assert.Contains("global::Rask.Core.ScopedAssets.ScopedAssetRegistry.RegisterJs", generated);
        Assert.Contains("ModuleInitializer", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_DoesNotPairJsWithComponentInDifferentDirectory()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Other/Counter.cs", source) },
            new[] { ("/proj/Pages/Counter.js", "export function rendered(el) {}") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK017");
    }

    [Fact]
    public void Generator_RaisesRASK017_ForOrphanJsFile()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Conter.js", "export function rendered(el) {}") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK017");
    }

    [Fact]
    public void Generator_EscapesQuotesInJsContent()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.js", "export function rendered(el) { el.textContent = \"hi\"; }") });

        var generated = run.GeneratedSource("__RaskScopedJsRegistration");
        // verbatim string literal escapes " as ""
        Assert.Contains("\"\"hi\"\"", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_IgnoresWhitespaceOnlyJsFile()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.js", "   \n\t  ") });

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var anyRegistration = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Any(s => s.HintName.Contains("__RaskScopedJsRegistration"));
        Assert.False(anyRegistration);
    }

    [Fact]
    public void Generator_SkipsAbstractComponents()
    {
        const string source = """
                              namespace Foo;
                              public abstract class Base : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Base.cs", source) },
            new[] { ("/proj/Base.js", "export function rendered(el) {}") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK017");
    }

    private static GeneratorRun Run(
        (string Path, string Source)[] sources,
        (string Path, string Contents)[] additionalJs) =>
        GeneratorDriverFixture.Run(sources, new ComponentScopedJsGenerator(), additionalJs);
}
