using Rask.Core;
using B = Rask.Benchmarks.Infrastructure.Generated;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.Infrastructure;

/// <summary>
///     The page the load report drives: the same data-table shape
///     <see cref="FootprintApp" /> measures, wired for a real host.
/// </summary>
/// <remarks>
///     <para>
///         A separate type only because the hosted path builds its root through
///         <c>ActivatorUtilities.CreateInstance</c> and so needs the row count on the constructor, where
///         <see cref="FootprintApp" /> takes it as a factory parameter. The rendered shape is deliberately
///         identical, so a bytes-per-session number from this report is comparable with
///         <c>session-footprint</c>'s rather than a second, subtly different page.
///     </para>
///     <para>
///         The header counter is what makes each click produce a real frame: a render whose HTML matches
///         the last one is deduped and never reaches the wire, so a page without it would measure an
///         event loop that does no work.
///     </para>
/// </remarks>
public sealed class LoadPage(LoadPageOptions options) : Component
{
    private int _counter;

    protected override Component? Head => C.Title()["rask session load"];

    protected override Component? Render()
    {
        var rows = new List<Component>(options.RowCount);
        for (var i = 0; i < options.RowCount; i++)
        {
            rows.Add(B.FootprintRow(Index: i, Key: i));
        }

        return
        [
            C.Doctype(),
            C.Html()[
                C.Head(),
                C.Body()[
                    C.Div(Class: "container", Id: "root")[
                        // The first handler in document order — the one the client finds and clicks.
                        C.Div(Class: "header")[
                            C.Button(OnClick: () => _counter++)[$"rows={options.RowCount} counter={_counter}"]
                        ],
                        C.Table(Class: "table")[C.Tbody()[rows]]
                    ]
                ]
            ]
        ];
    }
}
