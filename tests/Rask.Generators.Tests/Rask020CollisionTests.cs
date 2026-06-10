using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

/// <summary>
///     Covers RASK020 — JS-side simple-name collision diagnostic emitted by
///     <see cref="ComponentScopedJsGenerator" /> when two or more components with scoped
///     JS share the same simple type name. Browser-side <c>window.Rask[{Name}]</c> only
///     has one slot per name, so the second registration silently overwrites the first.
/// </summary>
public class Rask020CollisionTests
{
    [Fact]
    public void TwoComponentsSameSimpleName_DifferentNamespaces_BothHaveJs_FiresRask020Warning()
    {
        var run = Run(
            new[]
            {
                ("/proj/PageA/Counter.cs",
                    "namespace A; public sealed class Counter : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }"),
                ("/proj/PageB/Counter.cs",
                    "namespace B; public sealed class Counter : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }")
            },
            new[]
            {
                ("/proj/PageA/Counter.js", "export function f() {}"),
                ("/proj/PageB/Counter.js", "export function g() {}")
            });

        var diag = run.Diagnostics.First(d => d.Id == "RASK020");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        var msg = diag.GetMessage();
        // Message must call out both colliders by FQN so the user can act on it.
        Assert.Contains("A.Counter", msg);
        Assert.Contains("B.Counter", msg);
        Assert.Contains("Counter", msg);
    }

    [Fact]
    public void TwoComponentsSameSimpleName_OnlyOneHasJs_NoRask020()
    {
        var run = Run(
            new[]
            {
                ("/proj/PageA/Counter.cs",
                    "namespace A; public sealed class Counter : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }"),
                ("/proj/PageB/Counter.cs",
                    "namespace B; public sealed class Counter : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }")
            },
            new[]
            {
                ("/proj/PageA/Counter.js", "export function f() {}")
                // PageB/Counter has no .js sibling — only one registration → no collision.
            });

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK020");
    }

    [Fact]
    public void ThreeComponentsSameSimpleName_SingleDiagnostic_ListsAllThreeFqns()
    {
        var run = Run(
            new[]
            {
                ("/proj/A/Widget.cs",
                    "namespace A; public sealed class Widget : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }"),
                ("/proj/B/Widget.cs",
                    "namespace B; public sealed class Widget : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }"),
                ("/proj/C/Widget.cs",
                    "namespace C; public sealed class Widget : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }")
            },
            new[]
            {
                ("/proj/A/Widget.js", "export function f() {}"), ("/proj/B/Widget.js", "export function g() {}"),
                ("/proj/C/Widget.js", "export function h() {}")
            });

        // The fixture's Diagnostics property concatenates top-level + per-result, so a
        // single emission shows up multiple times. Distinct by message captures the
        // logical singleton — the underlying generator emits exactly one diagnostic per
        // collision group, listing all colliders in one message.
        var distinctMessages = run.Diagnostics
            .Where(d => d.Id == "RASK020")
            .Select(d => d.GetMessage())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Single(distinctMessages);
        Assert.Contains("A.Widget", distinctMessages[0]);
        Assert.Contains("B.Widget", distinctMessages[0]);
        Assert.Contains("C.Widget", distinctMessages[0]);
    }

    [Fact]
    public void DistinctSimpleNames_NoRask020()
    {
        var run = Run(
            new[]
            {
                ("/proj/A/Counter.cs",
                    "namespace A; public sealed class Counter : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }"),
                ("/proj/B/Toggle.cs",
                    "namespace B; public sealed class Toggle : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }")
            },
            new[]
            {
                ("/proj/A/Counter.js", "export function f() {}"), ("/proj/B/Toggle.js", "export function g() {}")
            });

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK020");
    }

    [Fact]
    public void Generator_EmitsScopedAssetRegistryRegistration()
    {
        var run = Run(
            new[]
            {
                ("/proj/Counter.cs",
                    "namespace Foo; public sealed class Counter : Rask.Core.Component { protected override Rask.Core.RenderResult Render() => this; }")
            },
            new[] { ("/proj/Counter.js", "export function rendered(el) {}") });

        var generated = run.GeneratedSource("__RaskScopedJsRegistration");
        Assert.Contains("global::Rask.Core.ScopedAssets.ScopedAssetRegistry.RegisterJs(typeof(global::Foo.Counter)",
            generated);
        Assert.DoesNotContain("ScopedJsRegistry", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    private static GeneratorRun Run(
        (string Path, string Source)[] sources,
        (string Path, string Contents)[] additionalJs) =>
        GeneratorDriverFixture.Run(sources, new ComponentScopedJsGenerator(), additionalJs);
}
