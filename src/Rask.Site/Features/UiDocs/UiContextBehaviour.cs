namespace Rask.Site.Features.UiDocs;

/// <summary>How a context menu behaves: what opens it, where it lands, and what the keyboard does.</summary>
internal sealed partial class UiContextBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("opening").Heading("Opening").Paragraphs([
                "The first child of `Ui.Context` is the area that is right-clicked, and the second is the `Ui.Menu`, the "
                + "same one a `Ui.Dropdown` opens, so the rows and the keyboard are identical: only the opening differs.",
                "The runtime opens it: an element carrying `data-rask-contextmenu=\"<popover id>\"` shows that popover "
                + "at the pointer in place of the browser's menu, straight away and on either host. A round trip first "
                + "would be a lag felt on every right-click.",
                "On a touch screen a long press opens it, and it stays open when the finger lifts, as Flux's does.",
                "Nothing in a context menu should be the only way to do something: iOS Safari never fires the event, so "
                + "put the same actions somewhere visible too.",
            ]),
            UiDocsTopic.Key("placement").Heading("Where it lands").Paragraphs([
                "Where Flux's does: below the pointer, with the menu's end edge on it. Near an edge of the viewport it "
                + "opens over the pointer, or with its start edge on it, and is slid back to stay 5px inside.",
                "`Position` (`BottomEnd`, `BottomCenter`, `BottomStart`, `TopEnd`, `TopCenter`, `TopStart`), `Gap` and "
                + "`Offset((x, y))` move it. `Target(\"id\")` opens a `Ui.Menu.Id(\"id\")` written somewhere else; "
                + "`Detail` lands on the menu as `data-detail`; `Disabled` leaves the browser's own menu alone.",
                "While it is open the page behind neither scrolls nor takes the pointer (`data-rask-lock`), as for a "
                + "dropdown's menu.",
            ]),
            UiDocsTopic.Key("keyboard").Heading("Keyboard").Paragraphs([
                "The ContextMenu key and Shift+F10 open it at the focused element, which is why the area is focusable. "
                + "Unlike Flux's, the menu takes focus as it opens, so the arrow keys work at once; from there every key "
                + "is the dropdown's.",
            ])
        ];
}
