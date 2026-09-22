using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK038 / RASK039. A required property is a required <i>parameter</i> on the generated factory,
///     so the language reports an omitted one; on the builder surface it is a setter somewhere in a
///     chain, so nothing does. These pin what the chain walk concludes — including the case where it
///     refuses to conclude anything.
/// </summary>
public class RequiredBuilderPropertyAnalyzerTests
{
    // `Card` has one required property — `Title`, non-nullable with no initializer, exactly RASK001's
    // rule — next to a nullable one and one with an initializer, which are not. The entries have the
    // shape the generator emits: `protected static` members named after their component type,
    // inherited rather than imported. So do the setters: extension methods in the global namespace
    // returning their receiver. `Panel` is the control — same type, name that is not its type's.
    private static string Source(string body) => $$"""
        using Rask.Core;

        namespace Demo
        {
            public sealed class Card : Component
            {
                public string Title { get; set; }
                public string? Note { get; set; }
                public string Kind { get; set; } = "plain";
            }

            public abstract class Entries : Component
            {
                protected static Card Card => null!;
                protected static Card Panel => null!;
            }

            public sealed class Page : Entries
            {
                protected override Component? Render()
                {
                    {{body}}
                }

                private static Component? Wrap(Component child) => child;
            }
        }

        public static class RaskBuilderSettersTest
        {
            public static global::Demo.Card Title(this global::Demo.Card c, string v) => c;
            public static global::Demo.Card Note(this global::Demo.Card c, string? v) => c;
            public static global::Demo.Card Kind(this global::Demo.Card c, string v) => c;
        }
        """;

    [Fact]
    public async Task A_chain_that_sets_the_required_property_raises_no_diagnostic() =>
        Assert.Empty(await Diagnostics(Source("""return Card.Title("Hi");""")));

    [Fact]
    public async Task The_chain_counts_the_required_setter_in_any_position() =>
        Assert.Empty(await Diagnostics(Source("""return Card.Note("n").Title("Hi").Kind("wide");""")));

    [Fact]
    public async Task A_bare_entry_with_a_required_property_reports_RASK038()
    {
        var d = Assert.Single(await Diagnostics(Source("return Card;")));
        Assert.Equal("RASK038", d.Id);
        Assert.Contains("'Title'", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'.Title(…)'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chain_that_sets_only_optional_properties_reports_RASK038() =>
        Assert.Equal("RASK038",
            Assert.Single(await Diagnostics(Source("""return Card.Note("n").Kind("wide");"""))).Id);

    [Fact]
    public async Task A_chain_used_as_an_argument_is_still_one_expression() =>
        Assert.Equal("RASK038",
            Assert.Single(await Diagnostics(Source("""return Wrap(Card.Note("n"));"""))).Id);

    [Fact]
    public async Task A_chain_stored_in_a_local_reports_RASK039_rather_than_a_wrong_answer()
    {
        var d = Assert.Single(await Diagnostics(Source("""
            var card = Card.Note("n");
            return card;
            """)));
        Assert.Equal("RASK039", d.Id);
        Assert.Contains("'Title'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chain_stored_in_a_local_but_already_complete_raises_no_diagnostic() =>
        Assert.Empty(await Diagnostics(Source("""
            var card = Card.Title("Hi");
            return card;
            """)));

    [Fact]
    public async Task A_property_whose_name_is_not_its_type_is_not_an_entry() =>
        // `Panel` is a Card-typed property, but an entry is the member whose name IS its component type.
        Assert.Empty(await Diagnostics(Source("""return Panel.Note("n");""")));

    [Fact]
    public async Task The_nameof_of_an_entry_is_not_a_chain() =>
        Assert.Empty(await Diagnostics(Source("""
            _ = nameof(Card);
            return null;
            """)));

    [Fact]
    public async Task A_component_with_no_required_property_is_never_reported() =>
        Assert.Empty(await Diagnostics("""
            using Rask.Core;

            namespace Demo
            {
                public sealed class Badge : Component
                {
                    public string? Label { get; set; }
                }

                public sealed class Page : Component
                {
                    public Badge Badge => null!;
                    protected override Component? Render() => Badge;
                }
            }
            """));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new RequiredBuilderPropertyAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
