using Rask.Core.Routing;

namespace Rask;

/// <summary>Flux's <c>flux:sidebar.item</c>: one destination in a <see cref="UiSidebarNav" />. Its children are its label.</summary>
/// <remarks>
/// A generated route is a <c>NavLink</c>: it navigates inside the app, and it is current — <c>aria-current="page"</c> — when
/// the router says so; <see cref="Current" /> states it instead. A string is an ordinary link the browser follows itself,
/// written as given. With no <see cref="Href" /> it is a button.
/// Narrowed to the rail it is its icon; the label stays for a screen reader.
/// </remarks>
public sealed partial class UiSidebarItem : Component
{
    private const string Root =
        "relative my-px flex h-10 items-center gap-3 rounded-lg border border-transparent text-zinc-500 "
        + "hover:bg-zinc-800/5 hover:text-zinc-800 sidebar-desktop:h-8 "
        + "sidebar-rail:w-10 sidebar-rail:justify-center sidebar-rail:px-3 "
        + "aria-[current=page]:border-zinc-200 aria-[current=page]:bg-white aria-[current=page]:text-zinc-800 "
        + "dark:text-white/80 dark:hover:bg-white/[7%] dark:hover:text-white "
        + "dark:aria-[current=page]:border-transparent dark:aria-[current=page]:bg-white/[7%] "
        + "dark:aria-[current=page]:text-white";

    /// <summary>Where the item leads. A generated route navigates inside the app; a string is an ordinary link.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>The icon before the label, and all that is left of the item in the rail.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>A short value at the item's trailing edge: a count, a state.</summary>
    public string? Badge { get; set; }

    /// <summary>Whether this is the page being shown. Unset, the router decides.</summary>
    public bool? Current { get; set; }

    /// <summary>What the item is called while the rail hides its label.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Classes for the link.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // Seam: Flux wraps every item in its tooltip, shown beside the rail. Ui.Tooltip goes here when it lands.
        Div.Class("min-w-0").Attributes(("data-ui-seam", "tooltip"))[Link()];

    private Component Link()
    {
        var classes = UiClass.Compose(Root, Badge is null ? "px-3" : "ps-3 pe-1.5", Class);
        var marks = UiMarks.Present(
            ("data-ui-sidebar-item", ""),
            ("title", Tooltip),
            ("aria-current", Current == true ? "page" : null));

        Component[] content =
        [
            Icon is { } icon ? Div.Class("relative")[Ui.Icon.Name(icon).Class("size-4")] : null!,
            Div.Class("flex-1 truncate text-sm font-medium sidebar-rail:sr-only")[Children ?? []],
            // Seam: Flux's navlist badge. Ui.Badge goes here when it lands.
            Badge is { } badge
                ? Span.Class(
                    "block min-w-5 rounded-sm bg-zinc-400/15 px-1 py-0.5 text-xs font-medium text-zinc-700 "
                    + "sidebar-rail:hidden dark:bg-zinc-400/40 dark:text-zinc-200")
                    .Attributes(("data-ui-seam", "badge"))[badge]
                : null!
        ];

        if (Href is not { } url)
        {
            var button = Button.Type(ButtonType.Button).Class(UiClass.Compose(classes, "w-full")).Attributes(marks);
            return button[content];
        }

        // A STRING is an ordinary link, written as given with no path base added (#1070): the way out of the app.
        if (url.PageType is null)
        {
            return A.Href(url.ToString()).Class(classes).Attributes(marks)[content];
        }

        // Stated: the router is not asked. Otherwise its class is what turns `aria-current` on.
        return NavLink.Href(url).ActiveClass(Current is null ? "current" : "").Class(classes).Attributes(marks)[content];
    }
}
