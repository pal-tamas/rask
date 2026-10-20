namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PaginationLinksExample : Component
{
    protected override Component? Render() =>
        Ui.Pagination
            .Paginator(new UiPaginator { Page = 1, PerPage = 5, Total = 20 })
            .Href(page => PageMeta.LinkTo(Routes.UiPaginationPage() with { QueryString = $"?page={page}" }));
}
