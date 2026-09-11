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
/// </remarks>
public sealed partial class UiPagination : Component
{
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
            Enumerable.Range(1, Math.Max(Pages, 0)).Select(page =>
                Href is { } href ? PageLink(page, href) : PageButton(page))
        ];

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
}
