using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Flux's <c>flux:navlist.item</c>: one place in a <see cref="UiNavlist" /> — an icon, its words and,
///     optionally, a count.
/// </summary>
/// <remarks>
///     <b><see cref="Current" /> is worked out for you.</b> Unset, a generated route compares itself with the page being shown.
///     Set it to say so yourself, as Flux's <c>current</c> does. The current row carries
///     <c>aria-current="page"</c> and is inked in the accent; <see cref="Accent" /> off inks it as the page is.
/// </remarks>
public sealed partial class UiNavlistItem : Component
{
    // The tinted row, then the outlined pill a navlist marked `outline` draws instead: keyed on the navlist's
    // own marker, so the variant is said once, on the list.
    private const string Base =
        "relative my-px flex h-8 items-center gap-3 rounded-lg px-3 text-zinc-500 hover:bg-zinc-800/[4%] "
        + "hover:text-zinc-800 dark:text-white/80 dark:hover:bg-white/[7%] dark:hover:text-white "
        + "aria-[current=page]:bg-zinc-800/[4%] dark:aria-[current=page]:bg-white/[7%] "
        + "[[data-ui-navlist=outline]_&]:border [[data-ui-navlist=outline]_&]:border-transparent "
        + "[[data-ui-navlist=outline]_&]:hover:bg-zinc-800/5 dark:[[data-ui-navlist=outline]_&]:hover:bg-white/[7%] "
        + "[[data-ui-navlist=outline]_&]:aria-[current=page]:border-zinc-200 "
        + "[[data-ui-navlist=outline]_&]:aria-[current=page]:bg-white "
        + "dark:[[data-ui-navlist=outline]_&]:aria-[current=page]:border-transparent "
        + "dark:[[data-ui-navlist=outline]_&]:aria-[current=page]:bg-white/[7%]";

    private const string AccentCurrent =
        "aria-[current=page]:text-fx-accent-content hover:aria-[current=page]:text-fx-accent-content";

    private const string PlainCurrent =
        "aria-[current=page]:text-zinc-800 hover:aria-[current=page]:text-zinc-800 "
        + "dark:aria-[current=page]:text-white dark:hover:aria-[current=page]:text-white";

    /// <summary>Where it goes. A generated route navigates inside the app; a string is an ordinary link.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>Whether this is the page being shown. Unset, it is worked out from the route.</summary>
    public bool? Current { get; set; }

    /// <summary>An icon before the words.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>A count or a word at the end of the row — "12", "Pro".</summary>
    public Component? Badge { get; set; }

    /// <summary>The badge's colour. Zinc when unset.</summary>
    public Ui.Color? BadgeColor { get; set; }

    /// <summary>How the badge is drawn.</summary>
    public Ui.NavBadgeVariant? BadgeVariant { get; set; }

    /// <summary>
    ///     Inks the current row in the accent. On unless this is <see langword="false" />, which inks it as the
    ///     page is.
    /// </summary>
    public bool? Accent { get; set; }

    /// <summary>Classes for the call site, added to the row's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component?[] content =
        [
            Icon is { } icon ? Div.Class("relative")[Ui.Icon.Name(icon).Class("size-4")] : null,
            // ui-rail-hide: a collapsed sidebar keeps the icon and drops the words.
            Div.Class("ui-rail-hide flex-1 whitespace-nowrap text-sm leading-none font-medium")
                .Attributes(("data-content", null))[Children ?? []],
            Badge is { } badge ? UiNavItemMarkup.Badge(badge, BadgeColor, BadgeVariant, "", marked: true) : null,
        ];

        return UiNavItemMarkup.Element(
            Href, Current,
            UiClass.Compose(Base, Accent == false ? PlainCurrent : AccentCurrent, Class),
            "data-ui-navlist-item", content);
    }
}
