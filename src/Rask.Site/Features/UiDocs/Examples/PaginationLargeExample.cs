namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PaginationLargeExample : Component
{
    private int _page = 1;

    protected override Component? Render() =>
        Ui.Pagination
            .Paginator(new UiPaginator { Page = _page, Total = 1000 })
            .OnPage(page => _page = page);
}
