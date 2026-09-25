using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Tests;

/// <summary>
///     Covers <c>[RaskChainGroup]</c>: a component library's entries live on a group class — <c>Ui.Button</c> — and
///     nowhere else, so the bare name (<c>Button</c>) stays with the HTML tag.
/// </summary>
public sealed class GroupedEntryTests
{
    private const string Library =
        """
        using Rask.Core;
        namespace Kit;

        public static partial class Ui;

        [RaskChainGroup(typeof(Ui))]
        public abstract class UiElement : Component;

        public sealed partial class UiButton : UiElement
        {
            public string? Tone { get; set; }
        }

        public sealed partial class UiCard : UiElement;

        public static partial class Trigger;

        [RaskChainGroup(typeof(Trigger))]
        public sealed partial class FullscreenTrigger : Component;

        [RaskChainGroup(typeof(Trigger), "Install")]
        public sealed partial class InstallPrompt : Component;
        """;

    [Fact]
    public void A_grouped_component_is_reached_through_its_group_and_the_tag_keeps_the_bare_name()
    {
        var compilation = BuilderGeneratorHarness.Compile(Library +
            """

            public sealed partial class Page : Component
            {
                protected override Component? Render() =>
                    Ui.Card[
                        Ui.Button.Tone("primary")["Save"],
                        Button["plain"],
                        Trigger.Fullscreen,
                        Trigger.Install
                    ];
            }
            """);

        Assert.Empty(Errors(compilation));
        AssertBinds(compilation, "Button[\"plain\"]", "Rask.Core.Components.HTMLButtonElement");
    }

    [Fact]
    public void A_grouped_component_has_no_bare_entry()
    {
        var compilation = BuilderGeneratorHarness.Compile(Library +
            """

            public sealed partial class Page : Component
            {
                protected override Component? Render() => UiButton["Save"];
            }
            """);

        // `UiButton` is only the type now; indexing a type is not an expression.
        Assert.NotEmpty(Errors(compilation));
        Assert.DoesNotContain(
            BuilderGeneratorHarness.Run(Library).Source("RaskBuilderEntryHost.g.cs").Split('\n'),
            line => line.Contains(" UiButton => ", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_components_on_one_member_are_RASK040()
    {
        var run = BuilderGeneratorHarness.Run(Library +
            """

            [RaskChainGroup(typeof(Ui), "Button")]
            public sealed partial class OtherButton : Component;
            """);

        Assert.Equal(2, run.WithId("RASK040").Count());
    }

    [Fact]
    public void A_group_that_is_not_partial_is_RASK036()
    {
        var run = BuilderGeneratorHarness.Run(
            """
            using Rask.Core;
            namespace Kit;

            public static class Ui { }

            [RaskChainGroup(typeof(Ui))]
            public sealed partial class UiButton : Component;
            """);

        var diagnostic = Assert.Single(run.WithId("RASK036"));
        Assert.Contains("Kit.Ui", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_in_a_referenced_library_is_reached_there_and_an_app_component_on_its_base_stays_bare()
    {
        var library = BuilderGeneratorHarness.Compile(Library, "KitLibrary");
        Assert.Empty(Errors(library));
        using var image = new MemoryStream();
        Assert.True(library.Emit(image).Success);

        const string app =
            """
            using Rask.Core;
            using Kit;
            namespace App;

            // Derives the library's grouped base, but the group is the library's and cannot be re-opened here.
            public sealed partial class Badge : UiElement;

            public sealed partial class Page : Component
            {
                protected override Component? Render() => Ui.Card[ Ui.Button["Save"], Badge ];
            }
            """;

        var run = BuilderGeneratorHarness.Run(app, [MetadataReference.CreateFromImage(image.ToArray())]);
        var trees = run.Sources.Select(s => s.SourceText.ToString()).Prepend(app)
            .Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)));
        var compilation = CSharpCompilation.Create(
            "TestAssembly", trees,
            GeneratorDriverFixture.BuildReferences().Add(MetadataReference.CreateFromImage(image.ToArray())),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        Assert.Empty(Errors(compilation));
    }

    [Fact]
    public void An_assembly_wide_group_takes_every_component_whose_name_carries_it_and_leaves_the_rest_bare()
    {
        var compilation = BuilderGeneratorHarness.Compile(
            """
            using Rask.Core;

            [assembly: RaskChainGroup(typeof(Kit.Ui))]

            namespace Kit;

            public static partial class Ui;

            public sealed partial class UiButton : Component
            {
                public string? Tone { get; set; }
            }

            public sealed partial class Card : Component;

            public sealed partial class Page : Component
            {
                protected override Component? Render() => Card[ Ui.Button.Tone("a")["Save"] ];
            }
            """);

        Assert.Empty(Errors(compilation));
    }

    [Theory]
    [InlineData("var b = Ui.Button.Tone(\"a\"); b.Tone = \"b\";")]
    // A grouped entry names itself at the symbol level, so even the step-less form is caught — which the bare
    // entry, a per-host forwarder, never could be.
    [InlineData("var b = Ui.Button; b.Tone = \"b\";")]
    public async Task A_write_after_a_grouped_chain_is_RASK045(string body)
    {
        var compilation = BuilderGeneratorHarness.Compile(Library +
            $$"""

            public sealed partial class Page : Component
            {
                protected override Component? Render()
                {
                    {{body}}
                    return null;
                }
            }
            """);
        Assert.Empty(Errors(compilation));

        var diagnostics = await compilation
            .WithAnalyzers([new Rask.Generators.Analyzers.ChainAssignedAfterwardsAnalyzer()], (AnalyzerOptions?)null)
            .GetAnalyzerDiagnosticsAsync();

        Assert.Equal("RASK045", Assert.Single(diagnostics).Id);
    }

    private static IEnumerable<string> Errors(Compilation compilation) =>
        compilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString());

    private static void AssertBinds(Compilation compilation, string expression, string type)
    {
        var tree = compilation.SyntaxTrees.First(t => t.ToString().Contains("class Page", StringComparison.Ordinal));
        var model = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ElementAccessExpressionSyntax>()
            .First(e => e.ToString() == expression);
        Assert.Equal(type, model.GetTypeInfo(node.Expression).Type?.ToDisplayString());
    }
}
