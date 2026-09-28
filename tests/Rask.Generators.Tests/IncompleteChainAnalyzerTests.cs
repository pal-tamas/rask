using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK095. A required property is a step the chain has to take before it is the component, so a skipped
///     one is a compile error the compiler words in generated type names — or, for a child, no error at all.
///     These pin that each shape gets the chain's own words instead, and that the fix takes the steps.
/// </summary>
public class IncompleteChainAnalyzerTests
{
    // `One` owes one required step, `Card` two, `Counter` one of a value type and `Editor` one of a
    // reference type that is not a string — the three placeholders the fix can write.
    private static string Source(string member) => $$"""
        using Rask.Core;
        namespace Demo;
        public sealed class Draft { }
        public partial class One : Component
        {
            public string Title { get; set; }
            public string? Note { get; set; }
            protected override Component? Render() => Div;
        }
        public partial class Card : Component
        {
            public string Title { get; set; }
            public string Body { get; set; }
            public string? Note { get; set; }
            protected override Component? Render() => Div;
        }
        public partial class Counter : Component
        {
            public int Count { get; set; }
            protected override Component? Render() => Div;
        }
        public partial class Editor : Component
        {
            public Draft Model { get; set; }
            protected override Component? Render() => Div;
        }
        public sealed partial class Page : Component
        {
            {{member}}
            private static Component Wrap(Component child) => child;
        }
        """;

    private static string Render(string expression) =>
        Source("protected override Component? Render() => " + expression + ";");

    [Theory]
    [InlineData("""One.Note("x")""", "'One' needs 'Title' before anything else — write One.Title(…).Note(…)")]
    [InlineData("""Card.Note("x")""", "'Card' needs 'Title' and 'Body' before anything else — write Card.Title(…).Body(…).Note(…)")]
    [InlineData("""Card.Title("t").Note("x")""", "'Card' needs 'Body' before anything else — write Card.Title(…).Body(…).Note(…)")]
    [InlineData("One", "'One' needs 'Title' — write One.Title(…)")]
    [InlineData("""Card.Title("t")""", "'Card' needs 'Body' — write Card.Title(…).Body(…)")]
    [InlineData("Div[One]", "'One' needs 'Title' — write One.Title(…)")]
    [InlineData("Div[One.Key(1)]", "'One' needs 'Title' — write One.Key(…).Title(…)")]
    [InlineData("Wrap(One)", "'One' needs 'Title' — write One.Title(…)")]
    public async Task A_chain_that_skips_a_required_step_says_which_step_in_the_chain_s_words(
        string expression, string message)
    {
        var source = Render(expression);

        var diagnostics = await Diagnostics(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK095", d.Id);
        Assert.Equal(message, d.GetMessage());
    }

    [Fact]
    public async Task A_half_built_chain_held_in_a_local_is_reported_where_it_is_used_as_a_child()
    {
        var source = Source("protected override Component? Render() { var c = One; return Div[c]; }");

        var diagnostics = await Diagnostics(source);

        Assert.Equal("'One' needs 'Title' — write c.Title(…)", Assert.Single(diagnostics).GetMessage());
    }

    [Theory]
    [InlineData("""Card.Title("t").Body("b")""")]
    [InlineData("""Card.Body("b").Title("t").Note("x")""")]
    [InlineData("""Div[One.Key(1).Title("t")]""")]
    [InlineData("""Div[Counter.Count(3), Editor.Model(new Draft())]""")]
    public async Task A_chain_that_takes_every_required_step_raises_no_diagnostic(string expression)
    {
        var source = Render(expression);

        var diagnostics = await Diagnostics(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_half_built_chain_stored_and_finished_later_raises_no_diagnostic()
    {
        var source = Source("""protected override Component? Render() { var c = One; return c.Title("t"); }""");

        var diagnostics = await Diagnostics(source);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("""One.Note("x")""", """One.Title("").Note("x")""")]
    [InlineData("Div[One]", """Div[One.Title("")]""")]
    [InlineData("""Card.Title("t")""", """Card.Title("t").Body("")""")]
    [InlineData("Div[Counter]", "Div[Counter.Count(default)]")]
    [InlineData("Div[Editor]", "Div[Editor.Model(default!)]")]
    public async Task The_fix_adds_the_missing_steps_right_after_the_chain_so_far(string written, string meant)
    {
        var source = Render(written);

        var fixedSource = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new IncompleteChainAnalyzer(), new IncompleteChainCodeFixProvider(), "RASK095", source);

        Assert.Contains("=> " + meant + ";", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fix_moves_a_required_step_taken_too_late_instead_of_inventing_a_value()
    {
        var source = Render("""Card.Note("x").Title("t")""");

        var fixedSource = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new IncompleteChainAnalyzer(), new IncompleteChainCodeFixProvider(), "RASK095", source);

        Assert.Contains("""=> Card.Title("t").Body("").Note("x");""", fixedSource, StringComparison.Ordinal);
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = BuilderGeneratorHarness.Compile(source);

        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new IncompleteChainAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        return all.Where(d => d.Id == "RASK095").ToImmutableArray();
    }
}
