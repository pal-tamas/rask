using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;
using Xunit;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK076 — a grid column with no field token, in a grid that lets the reader hide, reorder or group
///     columns by name. The token is the only name those menus have for a column, so a token-less one can
///     be shown and never hidden, moved or grouped: the menu is simply missing a row.
/// </summary>
/// <remarks>
///     <para>
///         Every case goes through a REAL chain ending in the grid's own indexer, because that is where
///         this analyzer would die quietly. Its predecessor RASK034 said the same thing about
///         <c>BsDataGrid</c> and, once the grid moved to a chain, <b>never fired again</b> — it read the
///         chain through the <c>Build&lt;T&gt;</c> helper, which matches nothing when the receiver is the
///         component itself. So the cases that matter most here are the ones asserting it DOES fire.
///     </para>
///     <para>
///         <c>AnalyzeAsync</c> fails the test if the stand-in source does not compile. Without that, every
///         "is not reported" case below would pass for the wrong reason — a file with a compile error
///         produces no analyzer diagnostics at all.
///     </para>
/// </remarks>
public class GridColumnTokenAnalyzerTests
{
    private const string Components = """
        namespace Rask.Ui
        {
            public sealed partial class UiColumn<T> : Rask.Core.Component
            {
                public string? Title { get; set; }
                public bool? Hideable { get; set; }
                public bool? Reorderable { get; set; }
                public bool? Groupable { get; set; }
            }

            public sealed partial class UiDataGrid<T, TKey> : Rask.Core.Component
            {
                public required System.Func<T, TKey> RowKey { get; set; }
                public System.Collections.Generic.IEnumerable<T>? Data { get; set; }
                public bool? ColumnChooser { get; set; }
                public bool? GroupPanel { get; set; }
                public System.Collections.Generic.IReadOnlyList<string>? HiddenColumns { get; set; }
                public System.Collections.Generic.IReadOnlyList<string>? ColumnOrder { get; set; }
                public System.Collections.Generic.IReadOnlyList<string>? Grouped { get; set; }

                public UiColumn<T> Field(
                    System.Linq.Expressions.Expression<System.Func<T, object?>> field) => null!;

                public UiColumn<T> Column() => null!;

                [Rask.Core.SkipFactory]
                public Rask.Core.Component this[
                    System.Func<UiDataGrid<T, TKey>,
                        System.Collections.Generic.IEnumerable<Rask.Core.Component?>> columns] => this;
            }
        }

        namespace Demo
        {
            public sealed record Row(int Id, string Name);

            public sealed partial class Board : Rask.Core.Component
            {
                public bool? ColumnChooser { get; set; }

                public Rask.Core.Component? Column() => null;
            }
        }
        """;

    // ---------- it fires ----------

    [Fact]
    public async Task A_tokenless_column_under_the_column_chooser_is_reported()
    {
        var d = Assert.Single(await AnalyzeAsync(
            """.ColumnChooser(true)[c => new[] { c.Field(r => r.Name), c.Column().Title("Actions") }]"""));

        Assert.Equal("RASK076", d.Id);
        Assert.Contains("column chooser", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Hideable(false)", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tokenless_column_under_the_group_panel_is_reported()
    {
        var d = Assert.Single(await AnalyzeAsync(
            """.GroupPanel(true)[c => new[] { c.Column().Title("Actions") }]"""));

        Assert.Contains("grouping", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Groupable(false)", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_controlled_hidden_column_list_counts_too() =>
        Assert.Single(await AnalyzeAsync(
            """.HiddenColumns(new[] { "Name" })[c => new[] { c.Column() }]"""));

    [Fact]
    public async Task A_controlled_column_order_counts_too() =>
        Assert.Single(await AnalyzeAsync(
            """.ColumnOrder(new[] { "Name" })[c => new[] { c.Column() }]"""));

    [Fact]
    public async Task A_controlled_grouping_counts_too() =>
        Assert.Single(await AnalyzeAsync(
            """.Grouped(new[] { "Name" })[c => new[] { c.Column() }]"""));

    [Fact]
    public async Task Opting_out_of_only_one_axis_still_reports_the_other()
    {
        // The chooser drives hiding AND reordering — the grid's own ReorderEnabled reads
        // `ColumnChooser is true || OrderControlled` — so saying the column is not hideable leaves the
        // reorder menu still unable to name it. RASK034 required both for exactly this reason.
        var d = Assert.Single(await AnalyzeAsync(
            """.ColumnChooser(true)[c => new[] { c.Column().Hideable(false) }]"""));

        Assert.Contains("column ordering", d.GetMessage(), StringComparison.Ordinal);
    }

    // ---------- it stays silent ----------

    [Fact]
    public async Task A_tokenless_column_in_a_plain_grid_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync(
            """[c => new[] { c.Field(r => r.Name), c.Column().Title("Actions") }]"""));

    [Fact]
    public async Task A_field_column_always_has_a_token_so_is_never_reported() =>
        Assert.Empty(await AnalyzeAsync(
            """.ColumnChooser(true).GroupPanel(true)[c => new[] { c.Field(r => r.Name) }]"""));

    [Fact]
    public async Task A_column_that_opted_out_of_every_axis_in_play_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync(
            """.ColumnChooser(true).GroupPanel(true)[c => new[] { c.Column().Hideable(false).Reorderable(false).Groupable(false) }]"""));

    [Fact]
    public async Task An_explicit_false_does_not_turn_an_axis_on() =>
        Assert.Empty(await AnalyzeAsync(
            """.ColumnChooser(false)[c => new[] { c.Column() }]"""));

    // The analyzer keys on the kit's own grid. Another component with a method spelled the same way is
    // none of its business, and matching on the name alone would make it so.
    [Fact]
    public async Task Another_components_identically_named_member_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync(null,
            "Board.ColumnChooser(true)"));

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string? gridChain, string? raw = null)
    {
        var expression = raw
                         ?? "UiDataGrid.Data(System.Array.Empty<Demo.Row>()).RowKey(r => r.Id)"
                         + gridChain;

        var page = $$"""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => {{expression}};
                }
            }
            """;

        var source = Components + "\n" + page;
        var run = BuilderGeneratorHarness.Run(source);
        var trees = run.Sources
            .Select(s => s.SourceText.ToString())
            .Prepend(source)
            .Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // A chain that does not compile produces no diagnostics, which would pass every "is not
        // reported" case above for entirely the wrong reason.
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
        {
            throw new Xunit.Sdk.XunitException(
                "The test source does not compile, so no analyzer result means anything:\n  "
                + string.Join("\n  ", errors.Select(e => e.ToString())));
        }

        var withAnalyzer = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new GridColumnTokenAnalyzer()));

        return await withAnalyzer.GetAnalyzerDiagnosticsAsync();
    }
}
