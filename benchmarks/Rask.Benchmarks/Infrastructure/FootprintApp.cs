using Rask.Core;
using Bench = Rask.Benchmarks.Infrastructure.Generated;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.Infrastructure;

/// <summary>
///     The page under measurement for the session capacity reports — a data-table page: a shell, a header
///     carrying a mutable counter, and <see cref="RowCount" /> keyed rows that each own an event handler.
///     <para>
///         One component shape swept by row count, rather than several hand-written pages, so that
///         <b>page size is the only independent variable</b> across the sweep. If the sizes differed in
///         shape too, a change in bytes-per-session couldn't be attributed to page size.
///     </para>
///     <para>
///         The rows are keyed and interactive on purpose: that is what a real data grid looks like, and it
///         is the shape the clean-subtree cache cannot help with. A component is only eligible for that
///         cache — which snapshots its subtree as a compact ~24 B/node <c>LeanFrame</c> span and releases
///         the Element object graph — when it has no nested user component, no handler, and no
///         <c>Key</c> (<c>Component.TryCacheCleanSubtree</c>). Since RASK022 pushes every list item toward
///         a <c>Key</c>, the pages where retained memory matters most are exactly the pages that keep their
///         Element graph for the session's lifetime. Measuring a keyless, handler-free table instead would
///         report a best case that few real pages hit.
///     </para>
/// </summary>
public sealed partial class FootprintApp : Component
{
    /// <summary>Counter rendered into the header. Bumped to make a render differ from the last one.</summary>
    public int Counter;

    // Non-nullable, no initializer → a required factory parameter (RASK001). Public so the generator
    // emits it; the reports build the page via Bench.FootprintApp(RowCount: n).
    public int RowCount { get; set; }

    protected override Component? Head => C.Title()["rask session footprint"];

    /// <summary>
    ///     Change the rendered HTML so the next render survives the session's dedup.
    ///     <c>LiveSession.RenderAndSendAsync</c> early-returns before building a payload when the
    ///     rendered HTML matches the baseline, so a session driven without this would never allocate
    ///     its payload buffers and would report a footprint that no real session has.
    /// </summary>
    public void Bump() => Counter++;

    protected override Component? Render()
    {
        var rows = new List<Component>(RowCount);
        for (var i = 0; i < RowCount; i++)
        {
            rows.Add(Bench.FootprintRow(Index: i, Key: i));
        }

        return
        [
            C.Doctype(),
            C.Html()[
                C.Head(),
                C.Body()[
                    C.Div(Class: "container", Id: "root")[
                        C.Div(Class: "header")[C.Span()[$"rows={RowCount} counter={Counter}"]],
                        C.Table(Class: "table")[C.Tbody()[rows]]
                    ]
                ]
            ]
        ];
    }
}

/// <summary>One table row: a few text cells and a select button.</summary>
public sealed partial class FootprintRow : Component
{
    public int Index { get; set; }

    protected override Component? Render() =>
        C.Tr(Class: "row")[
            C.Td()[$"#{Index}"],
            C.Td()[$"Item {Index}"],
            C.Td()[$"{Index * 37 % 1000} units"],
            C.Td()[C.Button(OnClick: () => { })["select"]]
        ];
}
