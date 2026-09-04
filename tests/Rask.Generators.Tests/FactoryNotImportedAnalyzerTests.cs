using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK043. The builder surface is reachable only from inside a component, because its entries are
///     inherited members — so a test class, a static markup helper or any other non-component reaches
///     components through the generated factory instead, behind an explicit <c>using static</c>. Without
///     it the simple name binds to the component TYPE and the compiler says CS0119 / CS0021, which names
///     neither Rask nor the import. These pin that the analyzer names both, and that it stays quiet
///     wherever the name really does resolve.
/// </summary>
public class FactoryNotImportedAnalyzerTests
{
    // A component library plus one caller. `Generated.Card` is the factory the caller is missing; it is
    // hand-written here rather than generated, since this test drives the ANALYZER, not the emission.
    private const string Library = """
        using Rask.Core;

        namespace Demo.Ui
        {
            public sealed class Card : Component { }

            public static class Generated
            {
                public static Card Card() => new();
            }
        }
        """;

    [Fact]
    public async Task A_component_named_in_a_non_component_without_the_import_reports_RASK043()
    {
        var d = Assert.Single(await Diagnostics("""
            using Demo.Ui;

            namespace Demo
            {
                public static class Parts
                {
                    public static object Build() => Card();
                }
            }
            """));

        Assert.Equal("RASK043", d.Id);
        Assert.Contains("'Card'", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Demo.Parts", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Rask.Core.RaskMarkup", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("CS0119", d.GetMessage(), StringComparison.Ordinal);
    }

    // The children indexer is the other half of a factory call, and a no-argument entry reaches the tree
    // through it alone — so it has to report as well.
    [Fact]
    public async Task The_children_indexer_on_a_component_type_reports_RASK043() =>
        Assert.Equal("RASK043", Assert.Single(await Diagnostics("""
            using Demo.Ui;

            namespace Demo
            {
                public static class Parts
                {
                    public static object Build() => Card["hi"];
                }
            }
            """)).Id);

    // The fix, and the reason the two-tier surface works at all: a factory is a METHOD, so C#'s
    // invocable-member rule lets it share its component's name where an entry property cannot.
    [Fact]
    public async Task Becoming_a_markup_host_silences_it() =>
        // The one fix left. The `using static …Generated;` that used to be the other went with the factory.
        Assert.Empty(await Diagnostics("""
            using Demo.Ui;

            namespace Demo
            {
                [Rask.Core.RaskMarkup]
                public static partial class Parts
                {
                    public static object Build() => Card;
                }
            }
            """));

    // Inside a component the entry is a member of the enclosing type and wins the lookup outright, so
    // this situation cannot arise there — and if it did, the answer would be the chain, not an import.
    [Fact]
    public async Task Inside_a_component_it_stays_quiet() =>
        Assert.Empty(await Diagnostics("""
            using Rask.Core;
            using Demo.Ui;

            namespace Demo
            {
                public sealed class Page : Component
                {
                    public static Card Card => null!;

                    protected override Component? Render() => Card;
                }
            }
            """));

    [Fact]
    public async Task A_type_that_is_not_a_component_is_not_its_business() =>
        Assert.Empty(await Diagnostics("""
            namespace Demo
            {
                public sealed class Ticket { }

                public static class Parts
                {
                    public static object? Build() => null;
                }
            }
            """));

    // A qualified call already says where it comes from — including the escape hatch the migration uses.
    [Fact]
    public async Task A_qualified_factory_call_is_not_reported() =>
        Assert.Empty(await Diagnostics("""
            namespace Demo
            {
                public static class Parts
                {
                    public static object Build() => Demo.Tw.Generated.Card();
                }
            }
            """));

    // The real thing, not a stand-in: this is the exact shape the discovery migration produced ~1,700
    // times, and `Rask.Core.Components.Div` is the type the name loses to.
    [Fact]
    public async Task The_real_Rask_Core_tags_are_reported_and_the_real_import_silences_it()
    {
        const string body = """
            namespace Demo
            {
                internal static class Parts
                {
                    public static object Loading() => Div(Class: "spinner");
                }
            }
            """;

        var d = Assert.Single(await Diagnostics("using Rask.Core.Components;\n" + body));
        Assert.Equal("RASK043", d.Id);
        Assert.Contains("Rask.Core.RaskMarkup", d.GetMessage(), StringComparison.Ordinal);

        // …and the fix it names actually silences it. There is one fix now: be a markup host. The
        // `using static …Generated;` that used to be the third option went with the factory.
        Assert.Empty(await Diagnostics(
            "using Rask.Core.Components;\n"
            + body.Replace("internal static class Parts", "[Rask.Core.RaskMarkup] internal static partial class Parts")));
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(Library, new CSharpParseOptions(LanguageVersion.Latest)),
                CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)),
            },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new FactoryNotImportedAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
