using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's pagination as a C# component: each example with its own code, and its reference.</summary>
[Route("pagination")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiPaginationPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Pagination.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Pagination)
            .Lead("Where a list is, and the controls that move through it. Flux hands its pager a Laravel paginator; "
                  + "Rask hands it a UiPaginator, and the page decides what each page shows.")
            .Api(UiApi.Pagination)[
            UiDocsExample.Key("ui-pagination").Heading("Pagination").File("PaginationExample.cs").Demo(PaginationExample).Testid("ui-pagination")
                .Blurb("A paginator that knows its total: the summary, and every page numbered once the pager has "
                       + "640px to itself."),
            UiDocsExample.Key("ui-pagination-simple").Heading("Simple paginator").File("PaginationSimpleExample.cs").Demo(PaginationSimpleExample).Testid("ui-pagination-simple")
                .Blurb("No total, for a list too large to count: only Previous and Next, and HasMore says whether "
                       + "Next leads anywhere."),
            UiDocsExample.Key("ui-pagination-large").Heading("Large result set").File("PaginationLargeExample.cs").Demo(PaginationLargeExample).Testid("ui-pagination-large")
                .Blurb("A thousand results at the default fifteen a page: the first two pages, the last two, and a "
                       + "window round the current one."),
            UiDocsExample.Key("ui-pagination-scroll").Heading("Scroll to top").File("PaginationScrollExample.cs").Demo(PaginationScrollExample).Testid("ui-pagination-scroll")
                .Blurb("ScrollTo names what to bring back into view when a page is chosen: the list being paged here."),
            UiDocsExample.Key("ui-pagination-links").Heading("Pages with an address").File("PaginationLinksExample.cs").Demo(PaginationLinksExample).Testid("ui-pagination-links")
                .Blurb("Given Href, every page is a link to where it lives: the page goes in the query string."),
            UiDocsExample.Key("ui-pagination-size").Heading("Rows per page").File("PaginationPageSizeExample.cs").Demo(PaginationPageSizeExample).Testid("ui-pagination-size")
                .Blurb("The page size is the paginator's PerPage, and the choice is a select of the page's own beside "
                       + "the pager. A new size goes back to the first page."),
            UiPaginationBehaviour
        ];
}
