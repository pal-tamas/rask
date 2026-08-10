using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     The builder-surface analyzers against the surface the GENERATOR actually emits, rather than
///     against a hand-written stand-in for it.
///     <para>
///         <see cref="BuilderEntryAliasAnalyzerTests" /> and
///         <see cref="RequiredBuilderPropertyAnalyzerTests" /> declare their own entries and setters,
///         which is the right way to pin the analyzers' rules — but it also means both would keep
///         passing if the emitted shape drifted away from the mirror, or if it had never matched. These
///         close that gap from the other end: the entries here are either the real ones baked into the
///         referenced <c>Rask.Core</c> (built with <c>RaskBuilderSurface=true</c>) or the ones
///         <see cref="ComponentFactoryGenerator" /> emits into this compilation.
///     </para>
/// </summary>
public class GeneratedBuilderSurfaceAnalyzerTests
{
    // The alias analyzer and the required-props analyzer both recognise an entry through
    // BuilderEntry.EntryTypeOf, so a single positive against the real emission covers both recognisers.
    [Fact]
    public async Task Alias_hidden_by_a_real_entry_on_Rask_Cores_Component_is_reported()
    {
        // Nothing here declares `B`: the entry is the `protected static Rask.Core.Components.B B` the
        // generator emitted into Rask.Core.RaskMarkup — Component's base, and where the framework
        // entries live so a type that is not a component can inherit them too — read back out of the
        // referenced assembly.
        var diagnostics = await AnalyzeAsync("""
            using B = Demo.Bench;

            namespace Demo
            {
                public static class Bench { public static string Name => "x"; }

                public sealed class Report : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => null;
                }
            }
            """);

        var d = Assert.Single(diagnostics.Where(x => x.Id == "RASK037"));
        Assert.Contains("Rask.Core.RaskMarkup", d.GetMessage(), StringComparison.Ordinal);
    }

    // The other half of the emission: a component the consumer declares gets its entry injected into
    // every other component's own partial, as `private static`. Different accessibility, different
    // declaring type, same shape rule.
    [Fact]
    public async Task Alias_hidden_by_a_generator_injected_consumer_entry_is_reported()
    {
        const string source = """
            using Card = Demo.Cards;

            namespace Demo
            {
                public static class Cards { public static string Name => "x"; }

                public sealed partial class Card : Rask.Core.Component
                {
                    public string? Note { get; set; }
                }

                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => null;
                }
            }
            """;

        var d = Assert.Single(
            (await AnalyzeGeneratedAsync(source)).Where(x => x.Id == "RASK037"));
        Assert.Contains("Demo.Page", d.GetMessage(), StringComparison.Ordinal);
    }

    // RASK038's chain walk over the generator's own entry and its own setters — no hand-written
    // `protected static Card Card => null!` and no hand-written extension class anywhere in the source.
    // `Title` is added by a second partial declaration AFTER generation, which is exactly the shape
    // CanHaveEntry withholds an entry for today (see the pin below): this is what the analyzer would be
    // asked to enforce the moment that restriction is lifted.
    [Fact]
    public async Task Required_property_missing_from_a_chain_over_a_generated_entry_is_RASK038()
    {
        var d = Assert.Single(
            (await AnalyzeGeneratedAsync(Chain, Chain + RequiredTitle)).Where(x => x.Id.StartsWith("RASK", StringComparison.Ordinal)));

        Assert.Equal("RASK038", d.Id);
        Assert.Contains("'Title'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_setter_named_in_the_chain_satisfies_RASK038()
    {
        // The chain walk assumes the emitted setter is an extension that hands its receiver back, which
        // is what lets `.Note(…)` be followed by another setter — assert that shape here rather than
        // trusting the mirror in RequiredBuilderPropertyAnalyzerTests to still describe it.
        Assert.Contains(
            "public static global::Demo.Card Note(this global::Demo.Card",
            BuilderGeneratorHarness.Run(Chain).Source("RaskBuilderSetters.g.cs"),
            StringComparison.Ordinal);

        var chain = Chain.Replace("Card.Note(\"n\")", "Card.Note(\"n\").Title(\"t\")", StringComparison.Ordinal);
        Assert.Empty((await AnalyzeGeneratedAsync(chain, chain + RequiredTitle))
            .Where(x => x.Id.StartsWith("RASK", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     The seam between the two halves of the builder surface, pinned so it cannot move silently.
    ///     <para>
    ///         A RASK001-required property used to withhold the entry outright, which left RASK038 with
    ///         nothing to fire on in generated code at all. It no longer does: the value is enforced at
    ///         the chain (here) and put back by the generated reset (the test below), which is the half
    ///         no call-site analyzer can reach. This asserts the two halves meet rather than assuming
    ///         either side of the seam.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task A_component_with_a_required_property_gets_an_entry__and_RASK038_walks_it()
    {
        const string source = """
            namespace Demo
            {
                public sealed partial class Card : Rask.Core.Component
                {
                    public string Title { get; set; }
                    public string? Note { get; set; }
                }

                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => Card.Note("n");
                }
            }
            """;

        var run = BuilderGeneratorHarness.Run(source);
        Assert.Contains(" Card =>", run.Source("RaskBuilderConsumerEntries.g.cs"), StringComparison.Ordinal);

        var d = Assert.Single((await AnalyzeGeneratedAsync(source)).Where(x => x.Id is "RASK038" or "RASK039"));
        Assert.Equal("RASK038", d.Id);
        Assert.Contains("'Title'", d.GetMessage(), StringComparison.Ordinal);
    }

    // The other half: the reset that stands in for the factory's required ARGUMENT. `default!` rather
    // than `default` because the property is non-nullable by definition — that is what the rule is.
    [Fact]
    public void A_required_property_is_put_back_by_the_generated_reset()
    {
        var setters = BuilderGeneratorHarness.Run("""
                                                  namespace Demo
                                                  {
                                                      public sealed partial class Card : Rask.Core.Component
                                                      {
                                                          public string Title { get; set; }
                                                      }
                                                  }
                                                  """).Source("RaskBuilderSetters.g.cs");

        Assert.Contains("__c.Title = default!;", setters, StringComparison.Ordinal);
    }

    // A chain over the generated `Card` entry, stored nowhere — one expression, so RASK038's walk is
    // complete and it is RASK038 rather than RASK039 that has to answer.
    private const string Chain = """
        namespace Demo
        {
            public sealed partial class Card : Rask.Core.Component
            {
                public string? Note { get; set; }
            }

            public sealed partial class Page : Rask.Core.Component
            {
                protected override Rask.Core.Component? Render() => Card.Note("n");
            }
        }
        """;

    private const string RequiredTitle = """

        namespace Demo
        {
            public sealed partial class Card
            {
                public string Title { get; set; }
            }
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params string[] sources)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            sources.Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest))),
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(
                new BuilderEntryAliasAnalyzer(), new RequiredBuilderPropertyAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }

    // Generates from <paramref name="generated" /> (default: the analysed source itself) and analyses
    // the emitted trees alongside <paramref name="analysed" />, so the entries and setters under test
    // are the generator's own output rather than a stand-in for it.
    private static Task<ImmutableArray<Diagnostic>> AnalyzeGeneratedAsync(string generated, string? analysed = null)
    {
        var run = BuilderGeneratorHarness.Run(generated);
        var trees = run.Sources
            .Select(s => s.SourceText.ToString())
            .Prepend(analysed ?? generated)
            .ToArray();
        return AnalyzeAsync(trees);
    }
}
