using Microsoft.CodeAnalysis;
using Rask.Generators.Translations;

namespace Rask.Generators.Tests;

/// <summary>
///     Proves that a generated catalog member actually <b>binds</b> from inside a markup host.
/// </summary>
/// <remarks>
///     <para>
///         Asserting that the generator emitted <c>Greeting</c> proves nothing about whether
///         <c>Strings.Greeting(x)</c> resolves where it is written. Inside a markup host the factory
///         generator injects a builder entry per component as a <em>member of the host type</em>, and
///         ordinary member lookup beats namespace-level names — which is exactly how seven analyzers in
///         this repo were once blind while their tests stayed green.
///     </para>
///     <para>
///         So these run both generators over one compilation, write the catalog by its <b>simple</b>
///         name (the shadowing-prone form real code uses), and ask the compiler rather than a string.
///         The test's own namespace matches the generator's default so that simple name is in scope.
///     </para>
/// </remarks>
public class TranslationsOnTheChainSurfaceTests
{
    private const string Catalog = """{ "Greeting": "Hello, {name}!", "Title": "Dashboard" }""";

    private static GeneratorRun Run(string body) =>
        GeneratorDriverFixture.Run(
            [("App.cs", $$"""
                using Rask.Core;

                namespace Rask.Generated;

                public partial class Page : Component
                {
                    protected override Component Render()
                    {
                        {{body}}
                    }
                }
                """)],
            [new ComponentFactoryGenerator(), new TranslationCatalogGenerator()],
            [("/p/Resources/Strings.en.json", Catalog)]);

    [Fact]
    public void A_catalog_member_binds_by_simple_name_inside_a_markup_host()
    {
        var run = Run("return Div[P[Strings.Title], P[Strings.Greeting(\"world\")]];");

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_missing_key_is_an_ordinary_C_sharp_compile_error()
    {
        // The whole point of generating members rather than looking strings up: a typo cannot reach a
        // running page. No Rask diagnostic is needed, because CS0117 already says it.
        var run = Run("return Div[P[Strings.Missing]];");

        Assert.DoesNotContain(run.Diagnostics, d => d.Id.StartsWith("RASK", StringComparison.Ordinal));
        Assert.Contains(run.GeneratedCompileErrors(), d => d.Id is "CS0117" or "CS1061");
    }

    [Fact]
    public void A_wrong_argument_count_is_an_ordinary_C_sharp_compile_error()
    {
        var run = Run("return Div[P[Strings.Greeting(\"a\", \"b\")]];");

        Assert.Contains(run.GeneratedCompileErrors(), d => d.Id is "CS1501" or "CS1503" or "CS1729");
    }
}
