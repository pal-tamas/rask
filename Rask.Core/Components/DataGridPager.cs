using Rask.Core.DataGrids;
using Rask.Core.Live;

namespace Rask.Core.Components;

// Renders pagination controls bound to the ambient DataGridContext. Resolves via the
// non-generic IDataGridPagingContext so the pager doesn't need to know TRow.
//
// Default template: <nav><button>Prev</button><span>Page X of Y</span><button>Next</button></nav>.
// Pass a Template lambda for full headless control — it receives the paging state plus
// callbacks for Prev/Next/Go(int).
public sealed class DataGridPager : Component
{
    public Func<DataGridPagerState, Component>? Template { get; set; }

    protected internal override bool BypassRenderCache => true;

    protected override Component Render()
    {
        var ctx = DataGridScope.CurrentPaging;
        if (ctx is null)
        {
            return new Fragment();
        }

        var state = new DataGridPagerState(
            ctx.CurrentPage,
            ctx.PageCount,
            ctx.PreviousPage,
            ctx.NextPage,
            ctx.GoToPage);

        if (Template is { } template)
        {
            return template(state);
        }

        var isFirst = ctx.CurrentPage <= 0;
        var isLast = ctx.CurrentPage >= ctx.PageCount - 1;

        return Components.Nav()[
            Components.Button(Type: "button", Disabled: isFirst, OnClick: () => ctx.PreviousPage())["Prev"],
            Components.Span()[$"Page {ctx.CurrentPage + 1} of {ctx.PageCount}"],
            Components.Button(Type: "button", Disabled: isLast, OnClick: () => ctx.NextPage())["Next"]
        ];
    }
}

public sealed record DataGridPagerState(
    int CurrentPage,
    int PageCount,
    Action Prev,
    Action Next,
    Action<int> Go);
