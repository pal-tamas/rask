using Microsoft.CodeAnalysis;
using Rask.Generators.External;

namespace Rask.Generators.Tests;

/// <summary>
///     Covers <see cref="ExternalGenerator" /> — the island diagnostics RASK056-059, and the
///     runtime-to-module inference that decides which front-end file the browser is pointed at.
/// </summary>
/// <remarks>
///     <para>
///         These had no coverage at all, which is how RASK057 came to be reported with RASK056's
///         DESCRIPTOR: the wrong id, the wrong title, and a one-placeholder message format handed
///         three arguments, so a prop with no wire encoding read as "must be declared 'partial'" and
///         RASK057 itself was unreachable. Nothing failed, because nothing looked.
///     </para>
///     <para>
///         Asserted through a real generator run rather than by reflecting over the descriptors: the
///         bug was in the CALL SITE, and every descriptor was perfectly well formed.
///     </para>
/// </remarks>
public class ExternalGeneratorTests
{
    [Fact]
    public void A_prop_with_no_wire_encoding_is_reported_as_RASK057()
    {
        var run = Run(
            """
            namespace App;

            public sealed partial class Chart : Rask.External.ReactComponent
            {
                public System.IO.Stream? Body { get; set; }
            }
            """);

        var diagnostic = Assert.Single(Distinct(run, "RASK057"));

        // The message names the component, the property, and why — all three of which were silently
        // dropped while this reported through a one-placeholder descriptor.
        Assert.Contains("Chart", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Body", diagnostic, StringComparison.Ordinal);

        // And it is emphatically NOT the partial rule, which is what it used to claim to be.
        Assert.DoesNotContain("partial", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_partial_island_is_reported_as_RASK056()
    {
        var run = Run(
            """
            namespace App;

            public sealed class Chart : Rask.External.ReactComponent
            {
            }
            """);

        var diagnostic = Assert.Single(Distinct(run, "RASK056"));
        Assert.Contains("Chart", diagnostic, StringComparison.Ordinal);
        Assert.Contains("partial", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_islands_sharing_a_simple_name_are_reported_as_RASK058()
    {
        var run = Run(
            """
            namespace A { public sealed partial class Chart : Rask.External.ReactComponent { } }
            namespace B { public sealed partial class Chart : Rask.External.ReactComponent { } }
            """);

        var diagnostic = Assert.Single(Distinct(run, "RASK058"));
        Assert.Contains("A.Chart", diagnostic, StringComparison.Ordinal);
        Assert.Contains("B.Chart", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void A_computed_module_override_is_reported_as_RASK059()
    {
        var run = Run(
            """
            namespace App;

            public sealed partial class Chart : Rask.External.ReactComponent
            {
                protected override string Module => "./" + System.Guid.NewGuid();
            }
            """);

        var diagnostic = Assert.Single(Distinct(run, "RASK059"));
        Assert.Contains("Chart", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void A_constant_module_override_is_accepted()
    {
        var run = Run(
            """
            namespace App;

            public sealed partial class Chart : Rask.External.ReactComponent
            {
                protected override string Module => "@acme/charts/Chart";
            }
            """);

        Assert.Empty(Distinct(run, "RASK059"));
    }

    [Theory]
    [InlineData("ReactComponent", "./Chart.tsx")]
    [InlineData("PreactComponent", "./Chart.tsx")]
    [InlineData("SolidComponent", "./Chart.tsx")]
    [InlineData("LitComponent", "./Chart.ts")]
    [InlineData("AngularComponent", "./Chart.ts")]
    [InlineData("VueComponent", "./Chart.vue")]
    [InlineData("SvelteComponent", "./Chart.svelte")]
    public void Each_runtime_infers_its_own_sibling_file(string baseClass, string module)
    {
        // The half nothing cross-checks: this side decides what the rendered markup POINTS AT, the
        // MSBuild globs decide what actually gets BUILT. A runtime whose extension disagreed would
        // ship markup naming a chunk the bundle never produced — and fail only in the browser.
        //
        // Only the module is generated. The runtime string itself is the BASE CLASS's, which is the
        // point of declaring it there, so it is asserted where it becomes observable: on the rendered
        // host element, in Rask.External.Tests.
        var run = Run(
            $$"""
              namespace App;

              public sealed partial class Chart : Rask.External.{{baseClass}}
              {
                  public int Value { get; set; }
              }
              """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains($"\"{module}\"", run.GeneratedSource("Chart"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ReactComponent", "react")]
    [InlineData("PreactComponent", "preact")]
    [InlineData("SolidComponent", "solid")]
    [InlineData("LitComponent", "lit")]
    [InlineData("AngularComponent", "angular")]
    [InlineData("VueComponent", "vue")]
    [InlineData("SvelteComponent", "svelte")]
    public void The_declared_runtime_is_carried_out_of_the_compilation(string baseClass, string runtime)
    {
        // Three runtimes share .tsx and two share .ts, so the MSBuild glob that discovers a file can
        // no longer tell which adapter it belongs to. This carrier is how the base class — the one
        // place the runtime cannot drift from what actually mounts — reaches the build.
        //
        // Without it the build falls back to the extension, and the wrong guess is silent all the way
        // to the browser: a Solid island handed React's adapter compiles, bundles, ships, loads, and
        // mounts nothing.
        var run = Run(
            $$"""
              namespace App;

              public sealed partial class Chart : Rask.External.{{baseClass}}
              {
                  public int Value { get; set; }
              }
              """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var carrier = run.GeneratedSource("RaskExternalIslands");
        Assert.Contains("internal static class RaskExternalIslands", carrier, StringComparison.Ordinal);

        // "runtime|module" in one constant: the reader wants both together, and a single field cannot
        // go half-missing the way two could.
        Assert.Contains($"public const string Chart = \"{runtime}|.", carrier, StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_that_is_not_an_island_is_left_alone()
    {
        var run = Run(
            """
            namespace App;

            public sealed class Plain : Rask.Core.Component
            {
                protected override Rask.Core.Component? Render() => null;
            }
            """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id.StartsWith("RASK05", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Distinct messages for one id. The fixture concatenates top-level and per-result diagnostics,
    ///     so a single emission surfaces more than once.
    /// </summary>
    private static string[] Distinct(GeneratorRun run, string id) =>
        run.Diagnostics
            .Where(d => d.Id == id)
            .Select(d => d.GetMessage())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static GeneratorRun Run(string source) =>
        GeneratorDriverFixture.Run(
            [("/proj/Chart.cs", source)],
            [new ExternalGenerator()]);
}
