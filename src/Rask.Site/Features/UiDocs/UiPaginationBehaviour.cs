namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How a pager behaves: what it numbers, buttons against links, the query string, paging on the server, under a
///     table, while the next page loads, the page size, and what a screen reader hears.
/// </summary>
internal sealed partial class UiPaginationBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Paginator(),
            Numbers(),
            Links(),
            Server(),
            Waiting(),
            PageSize(),
            Reading()
        ];

    private static UiDocsTopic Paginator() =>
        UiDocsTopic.Key("paginator").Heading("What a paginator knows").Paragraphs([
            "A `UiPaginator` is what a Laravel paginator tells Flux's pager: the page being shown, counted from one; "
            + "how many results a page holds, `PerPage`, fifteen when unset, as Laravel's is; and how many there are "
            + "in all, `Total`. With a total it is `Order::paginate()`. Without one it is `Order::simplePaginate()`, "
            + "for a list too long to count, and `HasMore` says whether Next leads anywhere. A page past the last is "
            + "held to the last.",
        ]);

    private static UiDocsTopic Numbers() =>
        UiDocsTopic.Key("numbers").Heading("What it numbers").Paragraphs([
            "A counted pager says \"Showing 16 to 30 of 240 results\" and draws Previous, Next and the pages between "
            + "them. Below fourteen pages every page is numbered. From fourteen it numbers the first two, the last "
            + "two and the current page with three either side, and writes `...` where pages are left out; within "
            + "seven pages of either end the window holds ten pages from that end instead, so the pager does not "
            + "shrink as you approach it.",
            "The numbers need room: the pager measures its own width (a container query, not the viewport), and under "
            + "640px draws Previous and Next alone, as 32px targets on a phone. The simple paginator has no summary "
            + "and no numbers at any width.",
        ]);

    private static UiDocsTopic Links() =>
        UiDocsTopic.Key("links").Heading("Buttons, or links in the query string").Paragraphs([
            "With `OnPage` each page is a `<button>` that reports the page chosen, and the page decides what to show. "
            + "With `Href` each is a link to where that page lives: shareable, bookmarkable, working before the "
            + "runtime boots, and `OnPage` is not called. Given both, the link wins: the client cancels the default "
            + "action of a click it dispatches, so a link that also ran a handler would stop navigating.",
            "That is how the page goes in the query string: `[QueryParam] public int Page { get; set; } = 1;` on the "
            + "page, and `.Href(page => Routes.LogsPage(Page: page))` on the pager. A generated route, or one "
            + "`with { QueryString = $\"?page={page}\" }`, is an in-app link under the app's path base, so it is right "
            + "wherever the app is mounted. A string, `.Href(page => $\"/logs?page={page}\")`, is an ordinary `<a>` "
            + "written exactly as given, with no path base added: the rule every linking component of the kit follows.",
        ]);

    private static UiDocsTopic Server() =>
        UiDocsTopic.Key("server").Heading("Paging on the server, and under a table").Paragraphs([
            "The pager pages nothing itself: ask the store for one page and for the count, and hand it the three "
            + "numbers. `var total = await Orders.Count();` and "
            + "`await Orders.OrderBy(o => o.Date).Skip((page - 1) * PerPage).Take(PerPage).ToList()`, then "
            + "`Ui.Pagination.Paginator(new UiPaginator { Page = page, PerPage = PerPage, Total = total }).OnPage(Load)`.",
            "`Ui.Table.Paginate(…)` takes the pager and draws it where Flux does: under the rows, outside the scroll "
            + "area, full width with its rule above, and it keeps its height when the box scrolls: "
            + "`Ui.Table.Paginate(Ui.Pagination.Paginator(orders).ScrollTo(\"#orders\").OnPage(Load)).Id(\"orders\")[…]`.",
            "`ScrollTo` brings something back into view when a page is chosen, for a pager at the foot of a long "
            + "table: `.ScrollTo(\"body\")` for the top of the document, `.ScrollTo(\"#orders\")` for the table. It is "
            + "a CSS selector, written as `data-rask-scroll-to` for the runtime, which scrolls on a press of any "
            + "button or link inside the pager.",
        ]);

    private static UiDocsTopic Waiting() =>
        UiDocsTopic.Key("waiting").Heading("While the next page loads").Paragraphs([
            "The pager's root carries `data-rask-loading=\"within\"`, so the runtime marks it with `data-loading` once "
            + "a press inside it has waited 200 ms, and takes the mark off when the page that answers it is whole. The "
            + "page number that was pressed is replaced by that page's render, which is why the mark lives on the "
            + "root. A table dims its rows by it; a list of your own can too, with the pager right after it: "
            + "`Div.Class(\"has-[+[data-ui-pagination][data-loading]]:opacity-50\")[rows]`.",
        ]);

    private static UiDocsTopic PageSize() =>
        UiDocsTopic.Key("size").Heading("Rows per page").Paragraphs([
            "Flux's pagination has no page-size prop, so the kit has none: the size is the paginator's `PerPage`, "
            + "and the choice is a `Ui.Select` of your own beside the pager, as in the example above. Go back to the "
            + "first page when it changes: the page you were on may not exist any more.",
        ]);

    private static UiDocsTopic Reading() =>
        UiDocsTopic.Key("reading").Heading("What a screen reader hears").Paragraphs([
            "Either way the current page is not a control: it says `aria-current=\"page\"`. On the first page "
            + "Previous is not one either, and on the last page Next: each keeps its place and says "
            + "`aria-disabled=\"true\"`.",
            "A counted pager labels its arrows `« Previous` and `Next »`, Laravel's translation, with the character a "
            + "reader hears rather than the name of its entity. The simple paginator's arrows carry no label, and its "
            + "spent arrow says nothing at all; a test finds them by position.",
        ]);
}
