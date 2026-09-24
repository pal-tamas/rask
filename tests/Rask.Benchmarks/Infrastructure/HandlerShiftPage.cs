using Rask.Core;

namespace Rask.Benchmarks.Infrastructure;

/// <summary>
///     A list of keyed, interactive rows with ONE conditional action above them. Toggling
///     <see cref="ShowToolbarAction" /> changes how many event handlers are registered before the walk
///     reaches the rows, while leaving every row's own markup untouched.
///     <para>
///         That is the shape behind the <c>HandlerShiftAboveList100</c> payload-bytes scenario. A toolbar
///         button that appears once something is selected, a "clear filter" affordance, a conditional row
///         control — all of them move the page's upstream handler count, and the question the scenario asks
///         is what the rows below cost when they do. The rows are <see cref="FootprintRow" />, the same
///         keyed handler-bearing row the session reports measure, so the two reports describe one page.
///     </para>
/// </summary>
public sealed partial class HandlerShiftPage : Component
{
    /// <summary>Set to render one extra button — one extra handler — above the rows.</summary>
    public bool ShowToolbarAction;

    // Non-nullable, no initializer → a required factory parameter (RASK001).
    public int RowCount { get; set; }

    protected override Component? Render()
    {
        var rows = new List<Component>(RowCount);
        for (var i = 0; i < RowCount; i++)
        {
            rows.Add(FootprintRow.Index(i).Key(i));
        }

        return
        [
            Doctype,
            Html[
                Head,
                Body[
                    Div.Class("wrap").Id("root")[
                        Div.Class("toolbar")[
                            Span["Rows"],
                            ShowToolbarAction ? Button.OnClick(() => { })["clear"] : null
                        ],
                        Table.Class("sheet")[Tbody[rows]]
                    ]
                ]
            ]
        ];
    }
}
