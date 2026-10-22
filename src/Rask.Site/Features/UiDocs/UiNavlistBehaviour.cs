namespace Rask.Site.Features.UiDocs;

/// <summary>How a navlist behaves: the current row, groups that fold, and its place in a sidebar's rail.</summary>
internal sealed partial class UiNavlistBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("current").Heading("The current row").Paragraphs([
                "A row is a `NavLink` underneath, so `Current` is worked out from the route and said with "
                + "`aria-current=\"page\"`, unless you state it, as Flux's `current` does: that is how a row stays "
                + "current across a whole section. The current row is inked in the accent; `Accent(false)` inks it as "
                + "the page is. The list is a `<nav>`, as Flux's is.",
            ]),
            UiDocsTopic.Key("groups").Heading("Groups that fold").Paragraphs([
                "A group is a heading over its rows. `Expandable()` makes it a disclosure: the heading becomes the "
                + "control, a chevron says which way it is, and a rule joins the rows under it. It is a `<details>`, "
                + "so it folds with a click, Enter or Space and no runtime at all, and is open unless "
                + "`Expanded(false)`, as Flux's is.",
                "`Expanded` with `OnExpandedChange` hands the state to the page: a navigation that opens the group "
                + "holding the current page, or a filter that opens every group with a match. Flux neither animates "
                + "nor remembers a fold, and neither does the kit.",
            ]),
            UiDocsTopic.Key("rail").Heading("In a sidebar that folds to a rail").Paragraphs([
                "Inside a `Ui.Sidebar` the list is the sidebar's own, `Ui.SidebarNav` of `Ui.SidebarItem`s and "
                + "`Ui.SidebarGroup`s, with the same `Href`, `Icon`, `Badge`, `Current`, `Heading`, `Expandable` and "
                + "`Expanded`. A sidebar that is `Collapsible(Ui.SidebarCollapsible.Always)` narrows to a 56px rail of "
                + "icons, and there each part changes shape.",
            ])[
                UiDocsKeys.Column("In the rail").Rows([
                    ("An item", "Is its icon. The label stays for a screen reader, the count is dropped, and the item's "
                                + "tooltip, a real `Ui.Tooltip` to the right of the icon, says the label (or `Tooltip`). "
                                + "Every item sits in that tooltip, as Flux's does, and it is drawn only while the "
                                + "sidebar is a rail."),
                    ("A group", "Is its `Icon`, and its rows are a menu beside it, Flux's `flux:dropdown "
                                + "position=\"right\" align=\"start\" hover`: it opens while the pointer is on the icon or "
                                + "the menu (only while the sidebar is a rail), on a press, and from the keyboard. The rows "
                                + "are the group's own, so they are written once."),
                    ("The group's keys", "Enter, Space or ArrowDown on the icon opens it, the arrows walk its rows, "
                                         + "Escape hands the focus back to the icon, and Tab moves on."),
                    ("A group without an icon", "Is not shown in the rail."),
                ]),
                Ui.Text.Class("mt-3")[
                    "The sidebar, its rail and how the reader's choice is remembered are the ",
                    Ui.Link.Href(PageMeta.LinkTo(Routes.UiSidebarPage()))["sidebar layout"],
                    "'s."
                ]
            ]
        ];
}
