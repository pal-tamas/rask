using Rask.Core;
using BI = Rask.Benchmarks.Infrastructure.Generated;
using C = Rask.Core.Components.Generated;

using CH = Rask.Html.Components.Generated;
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
///         The rows are keyed and interactive on purpose: that is what a real data grid looks like, and
///         what the clean-subtree cache has to earn its keep on. A component is eligible for that cache —
///         which snapshots its subtree as a compact ~24 B/node <c>LeanFrame</c> span and releases the
///         Element object graph — when it has no nested user component and no indexer children
///         (<c>Component.TryCacheCleanSubtree</c>); a <c>Key</c> and event handlers were each once
///         disqualifying and are no longer, so this shape does cache. Since RASK022 pushes every list
///         item toward a <c>Key</c>, measuring a keyless, handler-free table instead would report a best
///         case that few real pages hit.
///     </para>
/// </summary>
public sealed partial class FootprintApp : Component
{
    /// <summary>Counter rendered into the header. Bumped to make a render differ from the last one.</summary>
    public int Counter;

    /// <summary>
    ///     When set, the header carries one extra button — i.e. one extra event handler, rendered
    ///     <i>above</i> every row's own. Toggling it is how <c>session-churn</c>'s handler-shift pass makes
    ///     the page's upstream handler count move between renders, which is the case the clean-subtree
    ///     cache has to survive. Left false by every other report, and it emits nothing when false, so the
    ///     rest of the sweep measures exactly the page it measured before.
    /// </summary>
    public bool ShowExtraAction;

    // Non-nullable, no initializer → a required factory parameter (RASK001). Public so the generator
    // emits it; the reports build the page via BI.FootprintApp(RowCount: n).
    public int RowCount { get; set; }

    protected override Component? HeadAssets => C.Title()["rask session footprint"];

    /// <summary>
    ///     Change the rendered HTML so the next render survives the session's dedup.
    ///     <c>LiveSession.RenderAndSendAsync</c> early-returns before building a payload when the
    ///     rendered HTML matches the baseline, so a session driven without this would never allocate
    ///     its payload buffers and would report a footprint that no real session has.
    /// </summary>
    public void Bump() => Counter++;

    /// <summary>
    ///     Bump the counter <i>and</i> flip the extra header button, so the render differs from the last
    ///     one in the usual way AND the page's handler count changes by one above the rows.
    /// </summary>
    public void BumpWithHandlerShift()
    {
        Counter++;
        ShowExtraAction = !ShowExtraAction;
    }

    protected override Component? Render()
    {
        var rows = new List<Component>(RowCount);
        for (var i = 0; i < RowCount; i++)
        {
            rows.Add(BI.FootprintRow(Index: i, Key: i));
        }

        return
        [
            CH.Doctype(),
            C.Html()[
                C.Head(),
                C.Body()[
                    C.Div(Class: "container", Id: "root")[
                        C.Div(Class: "header")[
                            CH.Span()[$"rows={RowCount} counter={Counter}"],
                            ShowExtraAction ? C.Button(OnClick: () => { })["extra"] : null
                        ],
                        CH.Table(Class: "table")[CH.Tbody()[rows]]
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
        CH.Tr(Class: "row")[
            CH.Td()[$"#{Index}"],
            CH.Td()[$"Item {Index}"],
            CH.Td()[$"{Index * 37 % 1000} units"],
            CH.Td()[C.Button(OnClick: () => { })["select"]]
        ];
}
