using Rask.Core.Routing;

namespace Rask;

/// <summary>Flux's <c>flux:sidebar.item</c>: one destination in a <see cref="UiSidebarNav" />. Its children are its label.</summary>
/// <remarks>
/// <para>
/// A generated route is a <c>NavLink</c>: it navigates inside the app, and it is current — <c>aria-current="page"</c> — when
/// the router says so; <see cref="Current" /> states it instead. A string is an ordinary link the browser follows itself,
/// written as given. With no <see cref="Href" /> it is a button.
/// </para>
/// <para>
/// Narrowed to the rail it is its icon, and its label is the <see cref="UiTooltip" /> beside it: every item
/// sits in one, as Flux's does, and the tooltip is drawn only while the rail has hidden the label. Inside
/// the menu a <see cref="UiSidebarGroup" /> opens from the rail it is a row of that menu.
/// </para>
/// </remarks>
public sealed partial class UiSidebarItem : Component
{
    // In the rail it is a 40px square around its icon — but not in the menu a group opens from the rail,
    // which is inside the same sidebar: there it is a row of the menu, in the menu's ink.
    private const string Root =
        "relative my-px flex h-10 items-center gap-3 rounded-lg border border-transparent text-zinc-500 "
        + "hover:bg-zinc-800/5 hover:text-zinc-800 sidebar-desktop:h-8 "
        + "sidebar-rail:justify-center sidebar-rail:not-in-data-ui-menu:w-10 sidebar-rail:not-in-data-ui-menu:px-3 "
        + "aria-[current=page]:border-zinc-200 aria-[current=page]:bg-white aria-[current=page]:text-zinc-800 "
        + "dark:text-white/80 dark:hover:bg-white/[7%] dark:hover:text-white "
        + "dark:aria-[current=page]:border-transparent dark:aria-[current=page]:bg-white/[7%] "
        + "dark:aria-[current=page]:text-white "
        + "in-data-ui-menu:bg-white in-data-ui-menu:px-2 in-data-ui-menu:text-zinc-800 "
        + "in-data-ui-menu:hover:bg-zinc-50 in-data-ui-menu:data-active:bg-zinc-50 in-data-ui-menu:focus:outline-hidden "
        + "dark:in-data-ui-menu:bg-transparent dark:in-data-ui-menu:text-white "
        + "dark:in-data-ui-menu:hover:bg-zinc-600 dark:in-data-ui-menu:data-active:bg-zinc-600";

    private const string Words = "flex-1 truncate text-sm font-medium sidebar-rail:not-in-data-ui-menu:sr-only";

    /// <summary>
    ///     The tooltip of anything in a sidebar that the rail reduces to an icon: Flux opens it whenever the
    ///     pointer is there and draws it only while the sidebar is narrowed — and never inside a menu.
    /// </summary>
    internal const string RailTooltip = "hidden sidebar-rail:not-in-data-ui-menu:open:block";

    /// <summary>Where the item leads. A generated route navigates inside the app; a string is an ordinary link.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>The icon before the label, and all that is left of the item in the rail.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>A short value at the item's trailing edge: a count, a state.</summary>
    public string? Badge { get; set; }

    /// <summary>Whether this is the page being shown. Unset, the router decides.</summary>
    public bool? Current { get; set; }

    /// <summary>What the tooltip says while the rail hides the label. The label itself, unless this says otherwise.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Classes for the link.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.Tooltip.Position(Ui.TooltipPosition.Right).Class("block min-w-0")[
            Link(Context.Get<UiMenuLevel>()),
            Ui.TooltipContent.Class(RailTooltip)[Tooltip ?? UiMenuRow.Label(Children)]
        ];

    private Component Link(UiMenuLevel? menu)
    {
        var classes = UiClass.Compose(Root, Badge is null ? "px-3" : "ps-3 pe-1.5", Class);
        Component[] content =
        [
            Icon is { } icon ? Div.Class("relative")[Ui.Icon.Name(icon).Class("size-4")] : null!,
            Div.Class(Words).Attributes(("data-content", null))[Children ?? []],
            // The count Flux's navlist draws, which the rail drops with the label.
            Badge is { } count
                ? UiNavItemMarkup.Badge(count, color: null, variant: null, "block sidebar-rail:not-in-data-ui-menu:hidden", marked: true)
                : null!
        ];

        return AsRow(Anchor(classes), menu)[content];
    }

    // Stated: the router is not asked. Otherwise its class is what turns `aria-current` on.
    private string RouterClass => Current is null ? "current" : "";

    private Element Anchor(string classes)
    {
        if (Href is not { } url)
        {
            return Button.Type(ButtonType.Button).Class(UiClass.Compose(classes, "w-full"));
        }

        // A STRING is an ordinary link, written as given with no path base added (#1070): the way out of the
        // app. It carries no handler, so the browser follows it.
        return url.PageType is null
            ? A.Href(url.ToString()).Class(classes)
            : NavLink.Href(url).ActiveClass(RouterClass).Class(classes);
    }

    // In a menu the item is one of its rows: it takes a place in the keyboard's order and says so. A pick
    // closes the menu, which the runtime does for any row.
    private Element AsRow(Element element, UiMenuLevel? menu)
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Current == true)
        {
            aria["current"] = "page";
        }

        var data = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (menu is null)
        {
            data["ui-sidebar-item"] = "";
            return element.Aria(aria).Data(data);
        }

        var ordinal = menu.Scope.Register(menu.Parent, UiMenuRow.Label(Children), disabled: false, isSub: false);
        return UiMenuRow.Decorate(element, menu, ordinal, "menuitem", "ui-sidebar-item", aria, data);
    }
}
