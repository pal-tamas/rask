namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PaginationSimpleExample : Component
{
    private int _page = 1;

    protected override Component? Render() =>
        Ui.Pagination
            .Paginator(new UiPaginator { Page = _page, HasMore = _page < 5 })
            .OnPage(page => _page = page);
}
