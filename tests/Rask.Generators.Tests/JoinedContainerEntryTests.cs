using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.Tests;

/// <summary>
///     A seedless container and a generic component whose <c>[RaskChainEntry]</c> names it share one entry:
///     <c>Ui.Field[…]</c> is the container, as it always was, and <c>Ui.Field.Bind(() =&gt; m.Name)</c> opens the other.
/// </summary>
public sealed class JoinedContainerEntryTests
{
    private const string Library =
        """
        using System;
        using System.Linq.Expressions;
        using Rask.Core;

        [assembly: RaskChainGroup(typeof(Kit.Ui))]

        namespace Kit;

        public static partial class Ui;

        public enum FieldVariant { Block, Inline }

        public sealed partial class UiField : Component
        {
            public FieldVariant? Variant { get; set; }

            public string? Class { get; set; }

            public bool? Wide { get; set; }
        }

        [RaskChainEntry("UiField")]
        public sealed partial class UiFieldBound<T> : Component
        {
            public Expression<Func<T>> Bind { get; set; }

            public string? Label { get; set; }
        }

        public sealed class Model
        {
            public string Name { get; set; } = "";

            public int Age { get; set; }
        }
        """;

    [Fact]
    public void The_container_keeps_its_children_and_its_steps_on_the_joined_entry()
    {
        var compilation = BuilderGeneratorHarness.Compile(Library +
            """

            public sealed partial class Page : Component
            {
                protected override Component? Render() =>
                    Div[
                        Ui.Field[Span["a"]],
                        Ui.Field.Variant(FieldVariant.Inline).Class("x", null)[Span["b"]],
                        Ui.Field.Inline.Wide()[Span["c"]],
                        Ui.Field.Key(1)[Span["d"]]
                    ];
            }
            """);

        Assert.Empty(Errors(compilation));
        Assert.Equal("Kit.UiField", TypeOf(compilation, "Ui.Field.Variant(FieldVariant.Inline)"));
    }

    [Fact]
    public void Bind_on_the_joined_entry_opens_the_bound_component_with_its_type_read_from_the_lambda()
    {
        var compilation = BuilderGeneratorHarness.Compile(Library +
            """

            public sealed partial class Page : Component
            {
                private readonly Model _m = new();

                protected override Component? Render() =>
                    Div[
                        Ui.Field.Bind(() => _m.Name).Label("Name"),
                        Ui.Field.Key(2).Bind(() => _m.Age)
                    ];
            }
            """);

        Assert.Empty(Errors(compilation));
        Assert.Equal("Kit.UiFieldBound<string>", TypeOf(compilation, "Ui.Field.Bind(() => _m.Name)"));
        Assert.Equal("Kit.UiFieldBound<int>", TypeOf(compilation, "Ui.Field.Key(2).Bind(() => _m.Age)"));
    }

    [Fact]
    public void A_container_step_leaves_no_Bind_to_take()
    {
        var compilation = BuilderGeneratorHarness.Compile(Library +
            """

            public sealed partial class Page : Component
            {
                private readonly Model _m = new();

                protected override Component? Render() => Ui.Field.Class("x").Bind(() => _m.Name);
            }
            """);

        // The chain is the container from its first step on, and the container has no Bind.
        Assert.Contains(Errors(compilation), error => error.Contains("Bind", StringComparison.Ordinal));
    }

    [Fact]
    public void The_bare_joined_entry_is_still_the_container_where_one_is_wanted()
    {
        var compilation = BuilderGeneratorHarness.Compile(Library +
            """

            public sealed partial class Page : Component
            {
                private static UiField Field() => Ui.Field;

                protected override Component? Render() => Field();
            }
            """);

        Assert.Empty(Errors(compilation));
    }

    [Fact]
    public void A_container_step_an_opening_already_takes_is_RASK040()
    {
        var run = BuilderGeneratorHarness.Run(Library.Replace(
            "public bool? Wide { get; set; }", "public bool? Bind { get; set; }", StringComparison.Ordinal));

        Assert.Equal(2, run.WithId("RASK040").Count());
    }

    [Fact]
    public void Two_containers_on_one_joined_entry_are_RASK040()
    {
        var run = BuilderGeneratorHarness.Run(Library +
            """

            [RaskChainEntry("UiField")]
            public sealed partial class UiOtherField : Component;
            """);

        Assert.Equal(3, run.WithId("RASK040").Count());
    }

    private static IEnumerable<string> Errors(Compilation compilation) =>
        compilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString());

    private static string? TypeOf(Compilation compilation, string expression)
    {
        var tree = compilation.SyntaxTrees.First(t => t.ToString().Contains("class Page", StringComparison.Ordinal));
        var node = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .First(e => e.ToString() == expression);
        return compilation.GetSemanticModel(tree).GetTypeInfo(node).Type?.ToDisplayString();
    }
}
