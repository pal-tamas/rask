using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

public class ComponentScopedJsGeneratorTests
{
    [Fact]
    public void A_component_and_its_TypeScript_sibling_get_a_registration()
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
            new[] { ("/proj/Counter.ts", "export function rendered(el) { el.dataset.rendered = '1'; }") });
        var generated = run.GeneratedSource("__RaskScopedJsRegistration");

        Assert.Contains("typeof(global::Foo.Counter)", generated);
        Assert.Contains("el.dataset.rendered", generated);
        Assert.Contains("RegisterJs", generated);
        Assert.Contains("global::Rask.Core.ScopedAssets.ScopedAssetRegistry.RegisterJs", generated);
        Assert.Contains("ModuleInitializer", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TypeScript_is_not_paired_with_a_component_in_a_different_directory()
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
            new[] { ("/proj/Pages/Counter.ts", "export function rendered(el) {}") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK017");
    }

    [Fact]
    public void An_orphan_TypeScript_file_is_reported_as_RASK017()
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
            new[] { ("/proj/Conter.ts", "export function rendered(el) {}") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK017");
    }

    [Fact]
    public void Quotes_in_the_TypeScript_content_are_escaped()
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
            new[] { ("/proj/Counter.ts", "export function rendered(el) { el.textContent = \"hi\"; }") });
        var generated = run.GeneratedSource("__RaskScopedJsRegistration");

        // verbatim string literal escapes " as ""
        Assert.Contains("\"\"hi\"\"", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void A_whitespace_only_TypeScript_file_is_ignored()
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
            new[] { ("/proj/Counter.ts", "   \n\t  ") });

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var anyRegistration = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Any(s => s.HintName.Contains("__RaskScopedJsRegistration"));
        Assert.False(anyRegistration);
    }

    [Fact]
    public void Abstract_components_are_skipped_as_TypeScript_hosts()
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
            new[] { ("/proj/Base.ts", "export function rendered(el) {}") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK017");
    }

    /// <summary>
    ///     RASK055 fires for a `.js` sitting where a scoped asset would go.
    /// </summary>
    [Fact]
    public void RASK055_fires_for_a_js_sibling_of_a_component()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = GeneratorDriverFixture.RunScoped(
            new[] { ("/proj/Counter.cs", source) },
            [],
            strayJs: ["/proj/Counter.js"]);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK055");
    }

    /// <summary>
    ///     A `.js` with no component of that name beside it is somebody else's file, and is left alone.
    /// </summary>
    /// <remarks>
    ///     This is the case that decides where RASK055 lives. MSBuild can only see the filesystem, so
    ///     the best rule it could apply is "a .js next to a .cs" — which breaks a consumer whose
    ///     `Helpers.cs` is an ordinary static class and whose `Helpers.js` is a vendored script, with
    ///     no opt-out to reach for. Keying on "beside a Component subclass of that name" is semantic,
    ///     needs the compilation, and matches exactly the set of files that worked as scoped JS
    ///     before this change.
    /// </remarks>
    [Fact]
    public void RASK055_does_not_fire_for_a_js_with_no_component_of_that_name()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = GeneratorDriverFixture.RunScoped(
            new[] { ("/proj/Counter.cs", source) },
            [],
            strayJs: ["/proj/vendor-widget.js", "/proj/Helpers.js"]);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK055");
    }

    /// <summary>
    ///     Diagnostics point at the author's `.ts`, never at the compiled file in obj/.
    /// </summary>
    /// <remarks>
    ///     Worth pinning because getting it wrong breaks nothing visible: the build still fails, with
    ///     the right message, at a path the author has never seen and cannot edit. These diagnostics
    ///     used `Location.None` while the file csc held WAS the author's file; now that it is
    ///     generated output, the location has to be stated explicitly.
    /// </remarks>
    [Fact]
    public void RASK017_is_reported_at_the_ts_path_not_the_compiled_output()
    {
        var run = GeneratorDriverFixture.RunScoped(
            new[] { ("/proj/Unrelated.cs", "namespace Foo; public sealed class Unrelated { }") },
            new[] { ("/proj/Orphan.ts", "export function go() {}") });

        var diagnostic = run.Diagnostics.First(d => d.Id == "RASK017");

        Assert.EndsWith("Orphan.ts", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
        Assert.Contains("Orphan.ts", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("obj", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The registration carries the COMPILED JavaScript, not the TypeScript source.
    /// </summary>
    /// <remarks>
    ///     The browser never sees TypeScript, so embedding the source would ship type annotations to
    ///     the page and fail at parse time there — far from anything that names the cause.
    /// </remarks>
    [Fact]
    public void The_registration_embeds_the_compiled_text_not_the_source()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = GeneratorDriverFixture.RunScoped(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.ts", "export function rendered(el) { /* COMPILED-MARKER */ }") });
        var generated = run.GeneratedSource("__RaskScopedJsRegistration");

        Assert.Contains("COMPILED-MARKER", generated);
    }

    /// <summary>
    ///     Drives the generator the way the build does: over compiled output tagged with the `.ts`
    ///     it came from. Cases below name the `.ts`, which is what an author writes.
    /// </summary>
    private static GeneratorRun Run(
        (string Path, string Source)[] sources,
        (string Path, string Contents)[] scopedTs) =>
        GeneratorDriverFixture.RunScoped(sources, scopedTs);
}
