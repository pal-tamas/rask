using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

// RASK034 — a BsDataGrid using the column chooser/reorder, with a column that has no Field. The real
// Generated.BsDataGrid factory and BsColumn<T> live in Rask.Bootstrap (not referenced here), so the compilation
// carries minimal stand-ins: the analyzer keys on the symbol NAMES (a static Generated.BsDataGrid, a BsColumn),
// which the stubs reproduce faithfully enough to resolve.
public class DataGridColumnFieldAnalyzerTests
{
    private static string App(string call) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq.Expressions;

        namespace Rask.Bootstrap
        {
            public sealed class BsColumn<T>
            {
                public string Title { get; init; }
                public Func<T, object> Value { get; init; }
                public Expression<Func<T, object>> Field { get; init; }
                public bool Hideable { get; init; } = true;
                public bool Reorderable { get; init; } = true;
            }

            public static class Generated
            {
                public static object BsDataGrid<T>(
                    IEnumerable<T> Data = null,
                    IReadOnlyList<BsColumn<T>> Columns = null,
                    bool? ColumnChooser = null,
                    IReadOnlyList<string> HiddenColumns = null,
                    IReadOnlyList<string> ColumnOrder = null) => null;
            }
        }

        namespace Demo
        {
            using Rask.Bootstrap;
            using static Rask.Bootstrap.Generated;

            public sealed record Row(string Name, int Amount);

            public static class Use
            {
                public static object M() => {{call}};
            }
        }
        """;

    [Fact]
    public async Task ChooserOn_ColumnWithoutField_ReportsRask034()
    {
        // First column has no Field; the second does. Only the first is flagged.
        var d = Assert.Single(await Diagnostics(App(
            """
            BsDataGrid<Row>(ColumnChooser: true, Columns:
            [
                new BsColumn<Row> { Title = "Name", Value = r => r.Name },
                new BsColumn<Row> { Title = "Amount", Field = r => r.Amount },
            ])
            """)));

        Assert.Equal("RASK034", d.Id);
    }

    [Fact]
    public async Task ControlledHiddenColumns_TriggersTheCheck_Too()
    {
        var d = Assert.Single(await Diagnostics(App(
            """
            BsDataGrid<Row>(HiddenColumns: new[] { "amount" }, Columns:
            [
                new BsColumn<Row> { Title = "Name", Value = r => r.Name },
            ])
            """)));

        Assert.Equal("RASK034", d.Id);
    }

    [Fact]
    public async Task NoChooser_ColumnWithoutField_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App(
            """
            BsDataGrid<Row>(Columns:
            [
                new BsColumn<Row> { Title = "Name", Value = r => r.Name },
            ])
            """)));

    [Fact]
    public async Task ChooserOn_EveryColumnHasField_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App(
            """
            BsDataGrid<Row>(ColumnChooser: true, Columns:
            [
                new BsColumn<Row> { Title = "Name", Field = r => r.Name },
                new BsColumn<Row> { Title = "Amount", Field = r => r.Amount },
            ])
            """)));

    [Fact]
    public async Task ChooserOn_PinnedFixtureWithoutField_NoDiagnostic() =>
        // A column that opts out of both axes is a deliberate fixture — a missing Field is fine.
        Assert.Empty(await Diagnostics(App(
            """
            BsDataGrid<Row>(ColumnChooser: true, Columns:
            [
                new BsColumn<Row> { Title = "Name", Value = r => r.Name, Hideable = false, Reorderable = false },
            ])
            """)));

    [Fact]
    public async Task ChooserOn_ImplicitNewWithoutField_ReportsRask034()
    {
        // Target-typed `new()` in the collection expression is inspected the same way.
        var d = Assert.Single(await Diagnostics(App(
            """
            BsDataGrid<Row>(ColumnChooser: true, Columns:
            [
                new() { Title = "Name", Value = r => r.Name },
            ])
            """)));

        Assert.Equal("RASK034", d.Id);
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Disable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DataGridColumnFieldAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK034").ToImmutableArray();
    }
}
