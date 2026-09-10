using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The data grid, live.
/// </summary>
/// <remarks>
///     A page of its own rather than a section of Data display: it is the one component in the kit with
///     a chain shape of its own, and the reason for that shape is worth a paragraph beside a grid the
///     reader can actually sort.
/// </remarks>
[Route("ui/data-grid")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitDataGridPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "UI kit — Data grid — Rask",
            "UiDataGrid as a typed Rask component: sortable headers, paging, typed selection, "
            + "expandable detail rows, grouping with subtotals, a column chooser, and a card layout "
            + "on a phone.",
            Routes.UiKitDataGridPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Data grid"],
        P.Class("text-ui-muted")[
            "Columns arrive through a factory — ", Code["[c => [ c.Field(p => p.Name) ]]"], " — and the ",
            "lambda's parameter is the grid itself. That is not a style choice: C# infers a method's ",
            "type arguments from its own arguments and never from the target type of the indexer it ",
            "sits in, so a column written as a flat child has nothing to tell it what ", Code["p"],
            " is. The indexer is declared on ", Code["UiDataGrid<T, TKey>"], " itself, which scopes it ",
            "to a grid and nowhere else. It used to take a chain type of its own — ",
            Code["GridBuild<T, TKey>"], " — purely because an indexer cannot be constrained; the chain ",
            "receives on the component now, so declaring it there does the same job and costs no type ",
            "parameter."
        ],
        P.Class("mt-2 text-ui-muted")[
            "Every state axis is controlled or uncontrolled independently. Say nothing and the grid ",
            "holds its own sort, page, selection, grouping and column layout; name the state and its ",
            "callback and that one axis moves to the page. Below ", Code["sm"], " the table restyles ",
            "into stacked labelled lines — the same markup under different utilities, so the phone ",
            "layout costs nothing but the classes."
        ],
        CodeSample
            .Files(["UiKitDataGridDemo.cs"])
            .Notes("The selection's keys come back as an IReadOnlyList<int> because RowKey pinned the "
                + "chain's key type; the last grid's sort and page are plain fields on the demo. Every "
                + "interaction here is a C# handler, so this component — unlike most of the kit — needs "
                + "the runtime.")
            .Result(UiKitDataGridDemo)
    ];
}
