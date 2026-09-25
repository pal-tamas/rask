using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.Tests;

/// <summary>
///     Covers <c>Rask.Markup</c> as an app sees it — <c>using Rask;</c> and <c>using static Rask.Markup;</c>, the two lines
///     every template's GlobalUsings.cs carries: elements bare in a class that is not a component, <c>Html.X</c>
///     reaching an element a member hides, and the <c>&lt;html&gt;</c> element as <c>Html</c>.
/// </summary>
public sealed class HtmlClassTests
{
    private const string Usings =
        """
        using Rask;
        using Rask.Core;
        using static Rask.Markup;

        namespace App;

        """;

    [Fact]
    public void A_plain_class_writes_elements_and_primitives_bare()
    {
        var compilation = BuilderGeneratorHarness.Compile(Usings +
            """
            public static class EmptyState
            {
                public static Component For(string what) => Div.Class("empty")[P[$"No {what} yet"]];

                public static Component Note(string text) => Small[text];
            }
            """);

        Assert.Empty(Errors(compilation));
    }

    [Fact]
    public void Html_reaches_an_element_a_member_of_its_name_hides()
    {
        var compilation = BuilderGeneratorHarness.Compile(Usings +
            """
            public sealed partial class Card : Component
            {
                public string? Footer { get; set; }

                protected override Component? Render() => Markup.Footer[Footer ?? "none"];
            }
            """);

        Assert.Empty(Errors(compilation));
        Assert.Equal("Rask.Core.Components.HTMLElement", TypeOf(compilation, "Markup.Footer"));
    }

    [Fact]
    public void The_html_element_is_Document()
    {
        var compilation = BuilderGeneratorHarness.Compile(Usings +
            """
            public static class Shells
            {
                public static Component Root() => Html.Lang("en")[Head, Body["hi"]];
            }
            """);

        Assert.Empty(Errors(compilation));
        Assert.Equal("Rask.Core.Components.HTMLHtmlElement", TypeOf(compilation, "Html"));
    }

    private static IEnumerable<string> Errors(Compilation compilation) =>
        compilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString());

    private static string? TypeOf(Compilation compilation, string expression)
    {
        var tree = compilation.SyntaxTrees.First(t => t.ToString().Contains("namespace App", StringComparison.Ordinal));
        var model = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().First(e => e.ToString() == expression);
        return model.GetTypeInfo(node).Type?.ToDisplayString();
    }
}
