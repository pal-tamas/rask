using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     What RASK038 can and cannot see once the component lives in a REFERENCED assembly — the shape
///     every Rask.Bootstrap component has from an app's point of view.
/// </summary>
public class CrossAssemblyRequiredPropertyTests
{
    private const string Library = """
        using Rask.Core;

        namespace Lib
        {
            public sealed class Card : Component
            {
                public string Title { get; set; }
                public string Kind { get; set; } = "plain";
                public required string Slug { get; set; }
                public string? Note { get; set; }
            }
        }
        """;

    private const string Consumer = """
        using Rask.Core;

        namespace Demo
        {
            public abstract class Entries : Component
            {
                protected static Lib.Card Card => null!;
            }

            public sealed class Page : Entries
            {
                protected override Component? Render() => Card;
            }
        }
        """;

    [Fact]
    public async Task A_required_modifier_survives_metadata_so_RASK038_still_fires()
    {
        var d = Assert.Single(await Diagnostics());
        Assert.Equal("RASK038", d.Id);
        Assert.Contains("'Slug'", d.GetMessage(), StringComparison.Ordinal);
    }

    // What the factory generator emits into the library so the same question CAN be answered — see
    // EmitPublishedRequiredProperties. `Kind` carries an initializer and `Slug` carries the `required`
    // modifier, so neither is published: one is not required, the other already survives metadata.
    private const string Published = """
        [assembly: global::Rask.Core.RaskRequiredProperties("Lib.Card", "Title")]
        """;

    [Fact]
    public async Task A_RASK001_required_property_is_invisible_across_an_assembly_boundary()
    {
        // `Title` is non-nullable with no initializer — required by RASK001's rule, and reported when the
        // component is in the same compilation (RequiredBuilderPropertyAnalyzerTests). From metadata the
        // initializer is not observable, so `Title` and `Kind` look identical and neither is reported.
        var messages = (await Diagnostics()).Select(x => x.GetMessage()).ToList();
        Assert.DoesNotContain(messages, m => m.Contains("'Title'", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("'Kind'", StringComparison.Ordinal));
    }

    /// <summary>
    ///     …and what lifts that ceiling: the owning assembly publishes the answer it alone can compute, and
    ///     RASK038 reads it back. Same controlled pair, same consumer, one assembly attribute more.
    /// </summary>
    [Fact]
    public async Task A_published_RASK001_property_IS_reported_from_a_reference()
    {
        var messages = (await Diagnostics(Published)).Select(x => x.GetMessage()).ToList();
        var title = Assert.Single(messages, m => m.Contains("'Title'", StringComparison.Ordinal));
        Assert.Contains("'.Title(…)'", title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publishing_does_not_make_an_optional_property_required()
    {
        // `Kind` has an initializer and `Note` is nullable, so the owning compilation never published
        // either. Reading the published set is not a licence to guess about the rest.
        var messages = (await Diagnostics(Published)).Select(x => x.GetMessage()).ToList();
        Assert.DoesNotContain(messages, m => m.Contains("'Kind'", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("'Note'", StringComparison.Ordinal));
    }

    /// <summary>
    ///     A chain that names the published property is clean — the point is enforcement, not noise.
    /// </summary>
    [Fact]
    public async Task A_chain_that_sets_the_published_property_reports_nothing_extra()
    {
        var diagnostics = await Diagnostics(Published, """
            using Rask.Core;

            namespace Demo
            {
                public abstract class Entries : Component
                {
                    protected static Lib.Card Card => null!;
                }

                public static class Setters
                {
                    public static Lib.Card Title(this Lib.Card c, string v) { c.Title = v; return c; }
                    public static Lib.Card Slug(this Lib.Card c, string v) { c.Slug = v; return c; }
                }

                public sealed class Page : Entries
                {
                    protected override Component? Render() => Card.Title("a").Slug("b");
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    ///     The generator's half: it publishes the property a consumer cannot see, and only that one.
    /// </summary>
    [Fact]
    public void The_generator_publishes_the_invisible_kind_and_nothing_else()
    {
        var source = BuilderGeneratorHarness.Run("""
                                                 using Rask.Core;
                                                 namespace Lib
                                                 {
                                                     public partial class Card : Component
                                                     {
                                                         public string Title { get; set; }
                                                         public string Kind { get; set; } = "plain";
                                                         public string? Note { get; set; }
                                                     }
                                                 }
                                                 """).Source("RaskRequiredProperties.g.cs");

        Assert.Contains("""
                        [assembly: global::Rask.Core.RaskRequiredProperties("Lib.Card", "Title")]
                        """, source, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The two sides have to name the component the same way, and a generic one is where a name can
    ///     drift: the generator writes the key from the OPEN type (<c>Box&lt;T&gt;</c>) and the analyzer
    ///     reads it from the CONSTRUCTED one (<c>Box&lt;string&gt;</c>). One function computes both, and it
    ///     strips the type arguments and keeps the arity — so this pins that they meet.
    /// </summary>
    [Fact]
    public async Task A_generic_component_is_published_and_read_back_under_the_same_key()
    {
        var generated = BuilderGeneratorHarness.Run("""
                                                    using Rask.Core;
                                                    namespace Lib
                                                    {
                                                        public partial class Box<T> : Component
                                                        {
                                                            public string Title { get; set; }
                                                        }
                                                    }
                                                    """).Source("RaskRequiredProperties.g.cs");
        Assert.Contains("""
                        "Lib.Box`1", "Title"
                        """, generated, StringComparison.Ordinal);

        var diagnostics = await Diagnostics(generated, """
            using Rask.Core;

            namespace Demo
            {
                public sealed class Page : Component
                {
                    private static Lib.Box<string> Box => null!;

                    protected override Component? Render() => Box;
                }
            }
            """, library: """
            using Rask.Core;

            namespace Lib
            {
                public class Box<T> : Component
                {
                    public string Title { get; set; }
                }
            }
            """);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK038", d.Id);
        Assert.Contains("'Title'", d.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The other half of why <c>BlocksEntry</c> withholds an entry: an analyzer is not the only thing
    ///     in the way. <c>BuilderRuntime.Entry&lt;T&gt;</c> is constrained <c>where T : Component, new()</c>
    ///     and a type with a <c>required</c> member cannot satisfy <c>new()</c> at all, so those
    ///     components could not be handed an entry even if the call site were perfectly policed.
    /// </summary>
    [Fact]
    public void A_required_member_cannot_satisfy_the_new_constraint_Entry_needs()
    {
        var compilation = CSharpCompilation.Create(
            "NewConstraint",
            new[] { CSharpSyntaxTree.ParseText("""
                public class Card { public required string Title { get; set; } }
                public static class Runtime { public static T Entry<T>() where T : new() => new T(); }
                public static class Call { public static object Use() => Runtime.Entry<Card>(); }
                """, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        Assert.Contains(
            compilation.GetDiagnostics(),
            d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS9040");
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(
        string published = "", string? consumer = null, string? library = null)
    {
        var references = GeneratorDriverFixture.BuildReferences();
        var lib = CSharpCompilation.Create(
            "Lib",
            new[]
            {
                CSharpSyntaxTree.ParseText(library ?? Library, new CSharpParseOptions(LanguageVersion.Latest)),
                CSharpSyntaxTree.ParseText(published, new CSharpParseOptions(LanguageVersion.Latest)),
            },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = lib.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        stream.Position = 0;

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(consumer ?? Consumer, new CSharpParseOptions(LanguageVersion.Latest)),
            },
            references.Add(MetadataReference.CreateFromStream(stream)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new RequiredBuilderPropertyAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
