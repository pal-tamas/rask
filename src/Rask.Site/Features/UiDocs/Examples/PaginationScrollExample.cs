namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PaginationScrollExample : Component
{
    private const int PerPage = 5;

    private int _page = 1;

    private int First => ((_page - 1) * PerPage) + 1;

    protected override Component? Render() =>
        Div[
            Ol.Id("ui-pagination-rows").Class("mb-3 list-decimal space-y-1 ps-6 text-sm").Start(First)[
                Enumerable.Range(First, PerPage).Select(row => Li.Key(row)[$"Order {row}"])
            ],
            Ui.Pagination
                .Paginator(new UiPaginator { Page = _page, PerPage = PerPage, Total = 40 })
                .ScrollTo("#ui-pagination-rows")
                .OnPage(page => _page = page)
        ];
}
