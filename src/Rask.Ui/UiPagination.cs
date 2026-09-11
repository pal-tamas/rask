using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>
/// Numbered pages, as a joined row of buttons — or of links, given <see cref="Href" />.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI has no pagination component of its own — it is <c>join</c> plus buttons, which is what this
/// renders. The current page is a button that is <c>disabled</c> rather than merely styled: it is not an
/// action, and letting it be pressed re-navigates to where the reader already is.
/// </para>
/// <para>
/// <b>Buttons or links.</b> With <see cref="OnSelect" /> each page is a button that reports the choice, and the
/// page decides what to show. With <see cref="Href" /> each page is a link to where that page lives, which is
/// what paging should be wherever the page is in the URL: shareable, bookmarkable, reachable with the back
/// button, and working before the runtime has booted. Given both, the link wins — the client cancels the
/// default action of a click it dispatches, so a link that also ran a handler would stop navigating.
/// </para>
/// <para>
/// <b>A window, not every page.</b> A join is one unbreakable row, so a pager that drew all forty pages of a
/// log was wider than any phone and dragged the whole document sideways. Past seven pages it draws the first,
/// the last, and the current one with its neighbours, with a gap marker between — never more than seven
/// items, which fits a 360px screen.
/// </para>
/// </remarks>
public sealed partial class UiPagination : Component
{
    // First, gap, three around the current page, gap, last.
    private const int MaxItems = 7;

    public required int Pages { get; set; }

    public required int Current { get; set; }

    public Callback<int>? OnSelect { get; set; }

    /// <summary>Where each page lives, from its number counted from one. Makes every page a link.</summary>
    public Fn<int, RouteUrl>? Href { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("join", Class))
            .Aria(new Dictionary<string, string?> { ["label"] = "Pagination" })[
            Window(Math.Max(Pages, 0), Current).Select(item => item switch
            {
                Item.Gap gap => Gap(gap.Side),
                Item.Page page when Href is { } href => PageLink(page.Number, href),
                Item.Page page => PageButton(page.Number),
                _ => throw new InvalidOperationException("Unknown pager item."),
            })
        ];

    /// <summary>The pages to draw, in order, with a gap wherever a run of them is left out.</summary>
    private static IEnumerable<Item> Window(int pages, int current)
    {
        if (pages <= MaxItems)
        {
            for (var page = 1; page <= pages; page++)
            {
                yield return new Item.Page(page);
            }

            yield break;
        }

        var at = Math.Clamp(current, 1, pages);

        // Near either end the window slides against it, so the pager keeps the same number of items rather than
        // shrinking to "1 2 … 40" on the first page.
        var start = Math.Max(2, at - 1);
        var end = Math.Min(pages - 1, at + 1);
        if (at <= 3)
        {
            (start, end) = (2, 4);
        }
        else if (at >= pages - 2)
        {
            (start, end) = (pages - 3, pages - 1);
        }

        yield return new Item.Page(1);
        if (start > 2)
        {
            yield return new Item.Gap("start");
        }

        for (var page = start; page <= end; page++)
        {
            yield return new Item.Page(page);
        }

        if (end < pages - 1)
        {
            yield return new Item.Gap("end");
        }

        yield return new Item.Page(pages);
    }

    private Component PageButton(int page)
    {
        var button = Button
            .Key(page)
            .Type("button")
            .Class(UiClass.Compose("join-item btn", page == Current ? "btn-active" : ""))
            .Disabled(page == Current);

        if (OnSelect is { } select && page != Current)
        {
            button = button.OnClick(() => select.Invoke(page) ?? Task.CompletedTask);
        }

        return button[page.ToString(System.Globalization.CultureInfo.InvariantCulture)];
    }

    // The current page is not a link: it goes nowhere the reader is not already. It keeps the active look and
    // says where the reader is with aria-current instead. ActiveClass is emptied because NavLink matches the
    // query string exactly, and a first page whose URL carries no ?page= would otherwise light up on every page.
    private Component PageLink(int page, Fn<int, RouteUrl> href)
    {
        var label = page.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return page == Current
            ? Span.Key(page).Class("join-item btn btn-active").Attributes(("aria-current", "page"))[label]
            : NavLink.Key(page).Href(href.Invoke(page)).ActiveClass("").Class("join-item btn")[label];
    }

    // Decorative: the pages either side of it already say what was left out, so it is hidden from assistive
    // technology rather than announced as a control that does nothing.
    private static Component Gap(string side) =>
        Span.Key("gap-" + side).Class("join-item btn btn-disabled").Attributes(("aria-hidden", "true"))["…"];

    /// <summary>One slot in the pager: a page, or the gap standing in for the pages left out.</summary>
    private abstract record Item
    {
        internal sealed record Page(int Number) : Item;

        internal sealed record Gap(string Side) : Item;
    }
}
