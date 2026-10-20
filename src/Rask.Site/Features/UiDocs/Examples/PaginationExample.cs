namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PaginationExample : Component
{
    private int _page = 1;

    protected override Component? Render() =>
        Ui.Pagination
            .Paginator(new UiPaginator { Page = _page, PerPage = 5, Total = 24 })
            .OnPage(page => _page = page);
}
