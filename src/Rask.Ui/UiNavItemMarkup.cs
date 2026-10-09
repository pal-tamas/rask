using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     What a nav bar item and a navlist item share: the element one is written as, how it learns it is the
///     page being shown, and the count at its end.
/// </summary>
/// <remarks>
///     Flux marks the current item with <c>data-current</c>, which Livewire works out from the request.
///     Here the router does: a generated route is a <c>NavLink</c>, which compares itself with the page being
///     shown and writes <c>aria-current="page"</c> — so that attribute is what the kit's classes key on, and
///     what a stated <c>Current</c> writes too.
/// </remarks>
internal static class UiNavItemMarkup
{
    private static readonly (string, string?) CurrentPage = ("aria-current", "page");

    /// <summary>The item itself: a link when it has somewhere to go, a button when it only opens something.</summary>
    internal static Component Element(
        RouteUrl? href, bool? current, string classes, string marker, Component?[] content, UiInvoked? invoked = null)
    {
        if (href is not { } url)
        {
            // The marker first: `Attributes` replaces the bag, and what makes the item a dropdown's trigger is in it.
            var button = Markup.Button.Type(ButtonType.Button).Class(classes);
            button = current == true ? button.Attributes(CurrentPage, (marker, null)) : button.Attributes((marker, null));
            return (invoked is { } panel ? panel.On(button) : button)[content];
        }

        // A STRING is an ordinary link, written exactly as given with no path base added (#1070), and there is
        // no route to compare it with: its current state can only be stated.
        if (url.PageType is null)
        {
            var plain = Markup.A.Href(url.ToString()).Class(classes);
            return (current == true ? plain.Attributes(CurrentPage, (marker, null)) : plain.Attributes((marker, null)))[content];
        }

        // Stated: the call site owns it, so the route is not consulted (an empty ActiveClass is NavLink's opt-out).
        if (current is { } stated)
        {
            var link = Markup.NavLink.Href(url).ActiveClass("").Class(classes).Attributes((marker, null));
            return (stated ? link.Aria("current", "page") : link)[content];
        }

        // Worked out: NavLink compares the route and writes aria-current itself. `ui-current` is a name for it
        // to add, not a style — the look hangs off the attribute.
        return Markup.NavLink.Href(url).ActiveClass("ui-current").Class(classes).Attributes((marker, null))[content];
    }

    /// <summary>The count or word at an item's end, in Flux's badge colours. Zinc when no colour is named.</summary>
    internal static Component Badge(Component badge, Ui.Color? color, Ui.NavBadgeVariant? variant, string place, bool marked)
    {
        var span = Markup.Span.Class(
            "min-w-5 rounded-sm px-1 py-0.5 text-center text-xs font-medium",
            place,
            variant == Ui.NavBadgeVariant.Outline
                ? UiNavColors.BadgeOutline(color ?? Ui.Color.Zinc)
                : UiNavColors.Badge(color ?? Ui.Color.Zinc));

        // Flux marks the navlist's badge and leaves the nav bar's bare.
        return (marked ? span.Attributes(("data-ui-navlist-badge", null)) : span)[badge];
    }
}
