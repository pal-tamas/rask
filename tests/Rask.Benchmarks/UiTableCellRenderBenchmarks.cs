using BenchmarkDotNet.Attributes;
using Rask.Core;

namespace Rask.Benchmarks;

// A body of kit table cells rendered to HTML: 100 rows of 14 cells, so Allocated / 1,400 is the cost of one cell.
//
//   Plain       Ui.TableCell[text]                               — the kit's own class list alone
//   WithClass   Ui.TableCell.Class("truncate").Title(text)[text] — a call site's class joined to it (Eszaf's cells)
//   Strong      Ui.TableCell.Variant(Strong).Align(End)[text]   — another variant, to keep every path cached
[MemoryDiagnoser]
public partial class UiTableCellRenderBenchmarks : global::Rask.Core.RaskMarkup
{
    private const int Rows = 100;
    private const int Cells = 14;

    private static readonly string[] Values = [.. Enumerable.Range(0, Cells).Select(i => "Value " + i)];

    [Benchmark(Baseline = true)]
    public string Plain() => Body(text => Ui.TableCell[text]);

    [Benchmark]
    public string WithClass() => Body(text => Ui.TableCell.Class("truncate").Title(text)[text]);

    [Benchmark]
    public string Strong() => Body(text => Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Align(Ui.Align.End)[text]);

    private static string Body(Func<string, Component> cell) =>
        Ui.Table[
            Ui.TableRows[Enumerable.Range(0, Rows).Select(row => (Component)Ui.TableRow.Key(row)[Values.Select(cell)])]
        ].ToHtml();
}
