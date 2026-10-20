namespace Rask.Site.Features.UiDocs;

/// <summary>How a navbar behaves: the current item, a link against a button, and where it belongs.</summary>
internal sealed partial class UiNavbarBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("current").Heading("The current item").Paragraphs([
                "`Current` is worked out for you: an item given a generated route compares it with the page being "
                + "shown. Set it to say so yourself, as Flux's `current` does, which is how an item stays current "
                + "across a whole section, and how a string link (`Href(\"#\")` here) is marked. The current item "
                + "carries `aria-current=\"page\"`, is inked in the accent and underlined with it: a 2px bar 12px under "
                + "the item, the navbar's bottom edge. `Accent(false)` draws both in the page's ink.",
            ]),
            UiDocsTopic.Key("button").Heading("A link, or the button that opens a menu").Paragraphs([
                "An item with an `Href` is a link: a generated route navigates inside the app under its path base, "
                + "a string is an ordinary link. An item with none is a `<button>`, the trigger of a dropdown: "
                + "`Ui.Dropdown[Ui.NavbarItem.IconTrailing(Ui.IconName.ChevronDown)[\"Account\"], Ui.Navmenu[…]]`. "
                + "`Ui.Navmenu` is the dropdown's list of links, on the dropdown page.",
            ]),
            UiDocsTopic.Key("landmark").Heading("A landmark").Paragraphs([
                "The navbar is a `<nav>`, as Flux's is, so a screen reader lists it among the page's landmarks. The "
                + "same items in a column are `Ui.Navlist`; a header around them is the header layout's `Ui.Header`.",
            ])
        ];
}
