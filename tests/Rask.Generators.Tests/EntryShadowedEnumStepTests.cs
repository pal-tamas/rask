using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.Tests;

/// <summary>
///     An enum step named like an element (<c>Ui.TableColumn.Code</c>, <c>Ui.Button.Div</c>) used to be hidden inside
///     every markup host by the element entry the host inherits — <c>x.Code</c> found the static <c>&lt;code&gt;</c>
///     entry first, CS0176. These pin that such a step is a member of the component itself, that the element keeps its
///     bare name everywhere else, and that the step stays an extension wherever moving it could be misread.
/// </summary>
public sealed class EntryShadowedEnumStepTests
{
    private const string Library =
        """
        using Rask.Core;
        namespace Kit;

        public static partial class Ui;

        [RaskChainGroup(typeof(Ui))]
        public abstract class UiElement : Component;

        public enum ColumnWidth { Day, Code, Rest }

        public sealed partial class UiColumn : UiElement
        {
            public ColumnWidth? Width { get; set; }
        }
        """;

    [Fact]
    public void An_enum_step_named_like_an_element_compiles_and_sets_its_property()
    {
        var compilation = BuilderGeneratorHarness.Compile(Library + Page("Ui.Column.Code[\"Invoice\"]"));

        var step = BoundMember(compilation, "Ui.Column.Code");

        Assert.Empty(Errors(compilation));
        Assert.Equal("Kit.UiColumn", step?.ContainingType.ToDisplayString());
        Assert.Contains(
            "public new global::Kit.UiColumn Code => global::RaskBuilderSettersTestAssembly.Width(this, global::Kit.ColumnWidth.Code);",
            BuilderGeneratorHarness.Run(Library).Source("RaskEntryShadowedSteps.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_element_keeps_its_bare_name_in_a_component_beside_the_step()
    {
        var compilation = BuilderGeneratorHarness.Compile(Library + Page("Div[Ui.Column.Code[\"Invoice\"], Code[\"INV-1\"]]"));

        var element = ElementType(compilation, "Code[\"INV-1\"]");

        Assert.Empty(Errors(compilation));
        Assert.Equal("Rask.Core.HTMLElement", element);
    }

    [Fact]
    public void A_step_no_element_shadows_stays_an_extension()
    {
        var output = BuilderGeneratorHarness.Run(Library);

        var setters = output.Source("RaskBuilderSetters.g.cs");

        Assert.Contains("Day => __b.Width(global::Kit.ColumnWidth.Day);", setters, StringComparison.Ordinal);
        Assert.DoesNotContain("Code => __b.Width(", setters, StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_that_writes_the_element_bare_keeps_the_step_an_extension()
    {
        // Inside UiColumn a moved `Code` would turn `Code["x"]` into the column restyling itself, with a green build.
        const string source = """
                              using Rask.Core;
                              namespace Kit;

                              public enum ColumnWidth { Day, Code }

                              public sealed partial class UiColumn : Component
                              {
                                  public ColumnWidth? Width { get; set; }
                                  protected override Component? Render() => Code["x"];
                              }
                              """;

        var output = BuilderGeneratorHarness.Run(source);

        Assert.Contains("Code => __b.Width(global::Kit.ColumnWidth.Code);", output.Source("RaskBuilderSetters.g.cs"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(output.Sources, s => s.HintName.Contains("RaskEntryShadowedSteps", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unsealed_component_keeps_the_step_an_extension()
    {
        // A subclass would inherit the hiding member, and its own bare `Code` would quietly mean the step.
        var output = BuilderGeneratorHarness.Run(Library.Replace("public sealed partial class UiColumn", "public partial class UiColumn", StringComparison.Ordinal));

        var setters = output.Source("RaskBuilderSetters.g.cs");

        Assert.Contains("Code => __b.Width(global::Kit.ColumnWidth.Code);", setters, StringComparison.Ordinal);
        Assert.DoesNotContain(output.Sources, s => s.HintName.Contains("RaskEntryShadowedSteps", StringComparison.Ordinal));
    }

    private static string Page(string render) =>
        $$"""

          public sealed partial class Page : Component
          {
              protected override Component? Render() => {{render}};
          }
          """;

    private static IEnumerable<string> Errors(Compilation compilation) =>
        compilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString());

    private static (SemanticModel Model, SyntaxNode Root) PageTree(Compilation compilation)
    {
        var tree = compilation.SyntaxTrees.First(t => t.ToString().Contains("class Page", StringComparison.Ordinal));
        return (compilation.GetSemanticModel(tree), tree.GetRoot());
    }

    private static ISymbol? BoundMember(Compilation compilation, string expression)
    {
        var (model, root) = PageTree(compilation);
        var node = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().First(e => e.ToString() == expression);
        return model.GetSymbolInfo(node).Symbol;
    }

    private static string? ElementType(Compilation compilation, string expression)
    {
        var (model, root) = PageTree(compilation);
        var node = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().First(e => e.ToString() == expression);
        return model.GetTypeInfo(node.Expression).Type?.ToDisplayString();
    }
}
