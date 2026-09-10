using Rask.Core;

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
public sealed partial class LoadPage(LoadPageOptions options) : Component
{
    private int _counter;

    protected override Component? HeadAssets => Title["rask session load"];

    protected override Component? Render()
    {
        var rows = new List<Component>(options.RowCount);
        for (var i = 0; i < options.RowCount; i++)
        {
            rows.Add(FootprintRow.Key(i).Index(i));
        }

        return Div.Class("wrap").Id("root")[
            // The first handler in document order — the one the client finds and clicks.
            Div.Class("header")[
                Button.OnClick(() => _counter++)[$"rows={options.RowCount} counter={_counter}"]
            ],
            Table.Class("sheet")[Tbody[rows]]
        ];
    }
}
