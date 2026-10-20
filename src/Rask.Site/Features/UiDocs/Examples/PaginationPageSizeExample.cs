namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PaginationPageSizeExample : Component
{
    private int _page = 1;

    private int _perPage = 10;

    protected override Component? Render() =>
        Div.Class("flex items-center gap-4")[
            Ui.Select.Value(_perPage).OnChange(size => { _perPage = size; _page = 1; }).Class("w-24 shrink-0")[
                Ui.SelectOption.Key(10).Value(10)["10"],
                Ui.SelectOption.Key(25).Value(25)["25"],
                Ui.SelectOption.Key(50).Value(50)["50"]
            ],
            Ui.Pagination
                .Paginator(new UiPaginator { Page = _page, PerPage = _perPage, Total = 240 })
                .OnPage(page => _page = page)
                .Class("min-w-0 flex-1")
        ];
}
