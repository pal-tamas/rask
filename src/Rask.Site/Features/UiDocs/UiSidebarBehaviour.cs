namespace Rask.Site.Features.UiDocs;

/// <summary>How the sidebar layout behaves: two checkboxes, the rail, groups, the runtime's part and its limits.</summary>
internal sealed partial class UiSidebarBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("script").Heading("No script").Paragraphs([
                "Flux drives the sidebar with JavaScript; here each state is a checkbox inside the sidebar and each "
                + "control a `<label>` for it, so both work on a prerendered page before anything has loaded. Below "
                + "`Breakpoint` (`Lg`, 1024px, unless it says otherwise) the sidebar is off-screen, `Ui.SidebarToggle` "
                + "slides it over the page and a click on the backdrop slides it back. From `Breakpoint` up it is "
                + "docked, the toggle is not shown, and with `Collapsible(Ui.SidebarCollapsible.Always)` "
                + "`Ui.SidebarCollapse`, or a click anywhere on the rail, narrows it to a 56px rail of icons and widens "
                + "it again. The labels carry `role=\"button\"` and a tab stop, and the runtime presses them on Enter "
                + "and Space.",
                "One sidebar a page: the controls find it by fixed ids (`sidebar-open`, `sidebar-rail`), as Flux's find "
                + "it by a page-wide event. The sidebar's ground and border are your classes, as in Flux's examples.",
            ]),
            UiDocsTopic.Key("runtime").Heading("What the runtime keeps").Paragraphs([
                "The two states are the reader's, and neither is a prop, as neither is in Flux. A sidebar slid over the "
                + "page is put away when the app navigates, or when the row of the page already open is pressed "
                + "(`data-rask-uncheck-on-navigate`). The rail is remembered across visits in `localStorage` under "
                + "Flux's own key, `flux-sidebar-collapsed-desktop` (`data-rask-persist`); `Persist(false)` is Flux's "
                + "`persist=\"false\"`.",
                "A Server page restores the rail before its first paint. A WebAssembly app's runtime loads after the "
                + "prerendered page is on screen, so it adds `Ui.SidebarScript` to its `HeadAssets`, beside "
                + "`Ui.AppearanceScript`: a few lines that check the box as the parser reaches it, so a narrow sidebar "
                + "never opens wide and snaps shut. The same script keeps a press on the collapse control made before "
                + "the hooks have loaded.",
            ]),
            UiDocsTopic.Key("rail").Heading("Items and groups in the rail").Paragraphs([
                "`Ui.SidebarItem` is a `NavLink` underneath, so the current page is worked out from the route "
                + "(`aria-current=\"page\"`) unless `Current` states it. In the rail it is its icon: the label stays for "
                + "a screen reader, the badge is dropped, and the item's tooltip, a real `Ui.Tooltip` to the right of "
                + "the icon, says the label (or `Tooltip`). `Ui.SidebarCollapse` is named by its tooltip at every "
                + "width, \"Toggle sidebar\" unless `Tooltip` says otherwise, and `Ui.SidebarSearch` shows its "
                + "placeholder the same way.",
                "`Ui.SidebarGroup` is a heading over its items, or with `Expandable(true)` a native `<details>` that "
                + "folds with no round trip; `Expanded` and `OnToggle` hand that to C#. In the rail a group is its "
                + "`Icon`, and its items are a menu beside it: it opens while the pointer is on the icon or the menu, on "
                + "a press, and from the keyboard (Enter, Space or ArrowDown on the icon, the arrows through the rows, "
                + "Escape back to the icon). A group without an icon is not shown in the rail.",
            ]),
            UiDocsTopic.Key("parts").Heading("Search and profile").Paragraphs([
                "`Ui.SidebarSearch` is a button that looks like a field, as Flux draws it: the way into a search dialog "
                + "or a command palette that a `Ui.ModalTrigger` around it opens. Given `OnInput` it is a real field "
                + "for a sidebar that filters its own list.",
                "`Ui.SidebarProfile` draws the avatar (the picture, or the initials of `Name`), and a dropdown around it "
                + "makes it the trigger of the account menu. Narrowed to the rail it is the avatar alone.",
            ])
        ];
}
