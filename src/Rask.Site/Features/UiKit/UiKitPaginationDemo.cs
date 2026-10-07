namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux's pagination, example for example, and paged by the page's own fields.
/// </summary>
/// <remarks>
///     A pager shows a <see cref="UiPaginator" /> and reports the page chosen; what the list then shows is the
///     page's business. Here that is one <c>int</c> per example.
/// </remarks>
public sealed partial class UiKitPaginationDemo : Component
{
    private int _page = 1;
    private int _simple = 1;
    private int _large = 1;
    private int _scrolled = 1;

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("space-y-8")[
            Example(
                "ui-pagination",
                "Pagination",
                "A paginator that knows its total: the summary, and every page numbered once the pager has 640px to itself.",
                Ui.Pagination
                    .Paginator(new UiPaginator { Page = _page, PerPage = 5, Total = 24 })
                    .OnPage(page => _page = page)),
            Example(
                "ui-pagination-simple",
                "Simple paginator",
                "No total — counting a large list is expensive — so only Previous and Next, and HasMore says whether Next leads anywhere.",
                Ui.Pagination
                    .Paginator(new UiPaginator { Page = _simple, HasMore = _simple < 5 })
                    .OnPage(page => _simple = page)),
            Example(
                "ui-pagination-large",
                "Large result set",
                "Sixty-seven pages: the first two, the last two, and a window round the current page, with a gap where pages are left out.",
                Ui.Pagination
                    .Paginator(new UiPaginator { Page = _large, PerPage = 15, Total = 1000 })
                    .OnPage(page => _large = page)),
            Example(
                "ui-pagination-scroll",
                "Scroll to top",
                "ScrollTo names what to bring back into view when a page is chosen: \"body\" for the top of the document, or the list being paged.",
                Div[
                    Ol.Id("ui-pagination-rows").Class("mb-3 list-decimal space-y-1 ps-6 text-sm").Start(((_scrolled - 1) * 5) + 1)[
                        Enumerable.Range(((_scrolled - 1) * 5) + 1, 5).Select(row => Li.Key(row)[$"Order {row}"])
                    ],
                    Ui.Pagination
                        .Paginator(new UiPaginator { Page = _scrolled, PerPage = 5, Total = 40 })
                        .ScrollTo("#ui-pagination-rows")
                        .OnPage(page => _scrolled = page)
                ]),
            // Pages as links: each is the address that page lives at, so it can be shared and answers the back
            // button. The page you are on is not a link either — it says aria-current instead.
            Example(
                "ui-pagination-links",
                "Pages with an address",
                "Given Href, every page is a link instead of a button — what paging should be wherever the page is in the URL.",
                Ui.Pagination
                    .Paginator(new UiPaginator { Page = 1, PerPage = 5, Total = 20 })
                    .Href(page => PageMeta.LinkTo(Routes.UiKitNavigationPage() with { QueryString = $"?page={page}" })))
        ];

    private static Component Example(string testid, string heading, string blurb, Component pager) =>
        Div.Key(testid)[
            H3.Class("text-sm font-semibold")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            Div.Data("testid", testid)[pager]
        ];
}
