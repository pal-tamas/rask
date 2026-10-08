using System.Globalization;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Flux's <c>flux:pagination</c>: where a list is, and the buttons that move through it.
/// </summary>
/// <remarks>
///     <para>
///     Given a <see cref="UiPaginator" /> that knows its total it says "Showing 16 to 30 of 240 results" and
///     numbers the pages — every one below fourteen, and past that the first two, the last two and a window
///     round the current page with "..." where pages are left out. In a space narrower than 640px the
///     numbers give way to Previous and Next alone. A paginator with no total draws only those two.
///     </para>
///     <para>
///     <b>Buttons or links.</b> Flux's pager is buttons inside a Livewire component and links outside one.
///     With <see cref="OnPage" /> each page is a button that reports the page chosen, and the page decides
///     what to show. With <see cref="Href" /> each is a link to where that page lives: shareable,
///     bookmarkable, and working before the runtime has booted. Given both, the link wins — the client
///     cancels the default action of a click it dispatches, so a link that also ran a handler would stop
///     navigating.
///     </para>
/// </remarks>
public sealed partial class UiPagination : Component
{
    private static readonly UiPartMarker Marker = new("ui-pagination");

    private const string Counted =
        "@container flex items-center justify-between gap-3 border-t border-zinc-100 pt-3 dark:border-zinc-700";

    private const string Uncounted = "flex items-center justify-between border-t border-zinc-100 pt-3 dark:border-zinc-700";

    private const string Summary = "text-xs font-medium whitespace-nowrap text-zinc-500 dark:text-zinc-400";

    private const string Box =
        "flex items-center rounded-lg border border-zinc-200 bg-white p-px dark:border-white/10 dark:bg-white/10";

    // The two forms of a counted pager: Previous and Next alone, until its own box is 640px wide.
    private const string Narrow =
        "flex items-center rounded-lg border border-zinc-200 bg-white p-px dark:border-white/10 dark:bg-white/10 @[40rem]:hidden";

    private const string Wide =
        "hidden items-center rounded-lg border border-zinc-200 bg-white p-px dark:border-white/10 dark:bg-white/10 @[40rem]:flex";

    // A 32px target on a phone, 24px from sm up.
    private const string Arrow =
        "flex items-center justify-center rounded-md text-zinc-400 hover:bg-zinc-100 hover:text-zinc-800 "
        + "size-8 sm:size-6 dark:hover:bg-white/20 dark:hover:text-white";

    // The simple paginator's: white in dark, where a counted pager's arrows stay grey until hovered.
    private const string ArrowAlone =
        "flex items-center justify-center rounded-md text-zinc-400 hover:bg-zinc-100 hover:text-zinc-800 "
        + "size-8 sm:size-6 dark:text-white dark:hover:bg-white/20 dark:hover:text-white";

    private const string ArrowOff = "flex items-center justify-center rounded-md text-zinc-300 size-8 sm:size-6 dark:text-zinc-500";

    private const string PageButton =
        "h-6 rounded-md px-2 text-xs font-medium text-zinc-400 hover:bg-zinc-100 hover:text-zinc-800 "
        + "dark:hover:bg-white/20 dark:hover:text-white";

    // An <a> is inline, where a <button> centres its label in its box by nature.
    private const string PageLink =
        "inline-flex h-6 items-center justify-center rounded-md px-2 text-xs font-medium text-zinc-400 "
        + "hover:bg-zinc-100 hover:text-zinc-800 dark:hover:bg-white/20 dark:hover:text-white";

    private const string CurrentPage =
        "flex h-6 cursor-default items-center justify-center rounded-md px-2 text-xs font-medium text-zinc-800 dark:text-white";

    private const string Ellipsis =
        "flex size-6 cursor-default items-center justify-center rounded-md text-xs font-medium text-zinc-400";

    /// <summary>Where the list is and how far it runs: what a Laravel paginator tells Flux's.</summary>
    public required UiPaginator Paginator { get; set; }

    /// <summary>
    ///     A CSS selector to scroll into view when a page is chosen: <c>"body"</c> for the top of the
    ///     document, <c>"#orders"</c> for the table being paged.
    /// </summary>
    public string? ScrollTo { get; set; }

    /// <summary>The page the reader chose, counted from one. Each page is a button.</summary>
    public Callback<int> OnPage { get; set; }

    /// <summary>Where each page lives, from its number counted from one. Makes every page a link.</summary>
    public Fn<int, RouteUrl>? Href { get; set; }

    /// <summary>Classes for the call site, added to the pager's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var root = Div.Data(ScrollTo is { } target ? Marker.With(null, "rask-scroll-to", target) : Marker.With(null));

        // The simple paginator keeps the empty place of the summary, so its buttons stay at the far edge.
        return Paginator.LastPage is { } last
            ? root.Class(Counted, Class)[
                Div.Class(Summary)[Showing(Paginator)],
                Div.Class(Narrow)[Previous(), Next()],
                Div.Class(Wide)[Previous(), Pages(last), Next()]
            ]
            : root.Class(Uncounted, Class)[Div, Div.Class(Box)[Previous(), Next()]];
    }

    private static string Showing(UiPaginator paginator) => string.Create(
        CultureInfo.InvariantCulture,
        $"Showing {paginator.From} to {paginator.To} of {paginator.Total} results");

    private IEnumerable<Component> Pages(int last)
    {
        var gaps = 0;
        foreach (var page in UiPaginationWindow.Of(Paginator.Current, last))
        {
            yield return page switch
            {
                UiPaginationWindow.Gap => Div.Key(gaps++ == 0 ? "gap-start" : "gap-end").Class(Ellipsis).Aria("disabled", "true")["..."],
                _ when page == Paginator.Current => Div.Key(page).Class(CurrentPage).Aria("current", "page")[Number(page)],
                _ when Href is { } href => NavLink.Key(page).Href(href.Invoke(page)).ActiveClass("").Class(PageLink)[Number(page)],
                _ => Button.Key(page).Type(ButtonType.Button).Class(PageButton).OnClick(() => OnPage.Invoke(page))[Number(page)],
            };
        }
    }

    private Component Previous() =>
        Step("previous", "&laquo; Previous", Paginator.OnFirstPage ? null : Paginator.Current - 1, Ui.IconName.ChevronLeft, Ui.IconName.ChevronRight);

    private Component Next() =>
        Step("next", "Next &raquo;", Paginator.OnLastPage ? null : Paginator.Current + 1, Ui.IconName.ChevronRight, Ui.IconName.ChevronLeft);

    // A step with nowhere to go is not a control: it keeps its place. A counted pager names its steps and
    // says aria-disabled on a spent one; the simple one says neither. Both are Flux's live DOM, down to the
    // label, which is the text of Laravel's translation with its entity unresolved ("&laquo; Previous").
    // The arrow turns round where the page reads right to left.
    private Component Step(string key, string name, int? page, Ui.IconName arrow, Ui.IconName mirrored)
    {
        Component[] arrows =
        [
            Ui.Icon.Name(arrow).Micro.Class("rtl:hidden"),
            Ui.Icon.Name(mirrored).Micro.Class("hidden rtl:inline"),
        ];

        var counted = Paginator.LastPage is not null;
        var look = counted ? Arrow : ArrowAlone;
        var label = counted ? name : null;

        return page switch
        {
            null when counted => Div.Key(key).Class(ArrowOff).Aria(("disabled", "true"), ("label", label))[arrows],
            null => Div.Key(key).Class(ArrowOff)[arrows],
            { } to when Href is { } href => NavLink.Key(key).Href(href.Invoke(to)).ActiveClass("").Class(look).AriaLabel(label)[arrows],
            { } to => Button.Key(key).Type(ButtonType.Button).Class(look).AriaLabel(label).OnClick(() => OnPage.Invoke(to))[arrows],
        };
    }

    private static string Number(int page) => page.ToString(CultureInfo.InvariantCulture);
}
