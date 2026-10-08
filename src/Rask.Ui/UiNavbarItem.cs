using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Flux's <c>flux:navbar.item</c>: one link in a <see cref="UiNavbar" />, with an icon either side of its
///     words and a count after them.
/// </summary>
/// <remarks>
///     <para>
///     <b><see cref="Current" /> is worked out for you.</b> Unset, a generated route compares itself with the page being shown.
///     Set it to say so yourself, as Flux's <c>current</c> does. The current item carries
///     <c>aria-current="page"</c>, is inked in the accent and underlined with it; <see cref="Accent" /> off draws
///     both in the page's ink instead.
///     </para>
///     <para>
///     With no <see cref="Href" /> it is a <c>&lt;button&gt;</c> — the trigger of a dropdown.
///     </para>
/// </remarks>
public sealed partial class UiNavbarItem : Component
{
    private const string Base =
        "relative flex h-8 items-center rounded-lg px-3 text-zinc-500 hover:bg-zinc-800/5 hover:text-zinc-800 "
        + "dark:text-white/80 dark:hover:bg-white/10 dark:hover:text-white "
        + "aria-[current=page]:after:absolute aria-[current=page]:after:inset-x-0 aria-[current=page]:after:-bottom-3 "
        + "aria-[current=page]:after:h-[2px] hover:aria-[current=page]:bg-zinc-800/10 "
        + "dark:hover:aria-[current=page]:bg-white/10";

    private const string AccentCurrent =
        "aria-[current=page]:text-fx-accent-content hover:aria-[current=page]:text-fx-accent-content "
        + "aria-[current=page]:after:bg-fx-accent";

    private const string PlainCurrent =
        "aria-[current=page]:text-zinc-800 hover:aria-[current=page]:text-zinc-800 aria-[current=page]:after:bg-zinc-800 "
        + "dark:aria-[current=page]:text-white dark:hover:aria-[current=page]:text-white "
        + "dark:aria-[current=page]:after:bg-white";

    /// <summary>Where it goes. A generated route navigates inside the app; a string is an ordinary link.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>Whether this is the page being shown. Unset, it is worked out from the route.</summary>
    public bool? Current { get; set; }

    /// <summary>An icon before the words.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>An icon after the words — the chevron of an item that opens a menu.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>A count or a word after the label — "12", "Pro".</summary>
    public Component? Badge { get; set; }

    /// <summary>The badge's colour. Zinc when unset.</summary>
    public Ui.Color? BadgeColor { get; set; }

    /// <summary>How the badge is drawn.</summary>
    public Ui.NavBadgeVariant? BadgeVariant { get; set; }

    /// <summary>
    ///     Draws the current item in the accent. On unless this is <see langword="false" />, which draws it in the
    ///     page's own ink.
    /// </summary>
    public bool? Accent { get; set; }

    /// <summary>Classes for the call site, added to the item's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component?[] content =
        [
            Icon is { } icon ? Div.Class("relative")[Ui.Icon.Name(icon).Class("size-5")] : null,
            Label(),
            IconTrailing is { } trailing ? Ui.Icon.Name(trailing).Class("ms-1 size-5") : null,
            Badge is { } badge ? UiNavItemMarkup.Badge(badge, BadgeColor, BadgeVariant, "ms-2", marked: false) : null,
        ];

        return UiNavItemMarkup.Element(
            Href, Current,
            UiClass.Compose(Base, Accent == false ? PlainCurrent : AccentCurrent, Class),
            "data-ui-navbar-items", content);
    }

    // An item that is only an icon has no words to hold a place for.
    private Component? Label()
    {
        if (Children is null)
        {
            return null;
        }

        var words = Icon is null
            ? "flex-1 whitespace-nowrap text-sm leading-none font-medium"
            : "ms-3 flex-1 whitespace-nowrap text-sm leading-none font-medium";
        return Div.Class(words).Attributes(("data-content", null))[Children];
    }
}
