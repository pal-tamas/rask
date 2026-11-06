using BenchmarkDotNet.Attributes;
using Rask.Core;

namespace Rask.Benchmarks;

// A live page of 100 keyed kit rows, each holding unkeyed components — three cells and a button that acts on the
// row — re-rendered through the live root. The components inside a keyed row are identified by that row's key
// among the row's own children (#1309), so this is the cost of that identity on every render of a list:
//
//   Rerender         the same rows again: every unkeyed child finds the instance its row gave it last time
//   RemoveFirstRow   the first row goes and comes back, every other render: each row's children follow the row
[MemoryDiagnoser]
public partial class KeyedRowScopeBenchmarks : global::Rask.Core.RaskMarkup
{
    private KeyedRowScopePage _page = null!;

    [GlobalSetup]
    public void Setup()
    {
        _page = KeyedRowScopePage.RowCount(100);
        _ = _page.RenderAsLiveRoot();
    }

    [Benchmark(Baseline = true)]
    public string Rerender() => _page.RenderAsLiveRoot();

    [Benchmark]
    public string RemoveFirstRow()
    {
        _page.SkipFirst = !_page.SkipFirst;
        return _page.RenderAsLiveRoot();
    }
}

/// <summary>The page <see cref="KeyedRowScopeBenchmarks" /> renders: keyed rows of unkeyed kit components.</summary>
public sealed partial class KeyedRowScopePage : Component
{
    /// <summary>Leaves the first row out of the next render.</summary>
    public bool SkipFirst;

    // Non-nullable, no initializer → a required factory parameter (RASK001).
    public int RowCount { get; set; }

    protected override Component? Render() =>
    [
        Doctype,
        Html[
            Head,
            Body[
                Ui.Table[
                    Ui.TableRows[Enumerable.Range(SkipFirst ? 1 : 0, SkipFirst ? RowCount - 1 : RowCount).Select(i =>
                        (Component)Ui.TableRow.Key(i)[
                            Ui.TableCell[$"#{i}"],
                            Ui.TableCell[$"Item {i}"],
                            Ui.TableCell[Ui.Button.OnClick(() => SkipFirst = i == 0)["select"]]
                        ])]
                ]
            ]
        ]
    ];
}
