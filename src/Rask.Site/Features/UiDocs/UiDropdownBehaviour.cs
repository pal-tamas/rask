namespace Rask.Site.Features.UiDocs;

/// <summary>How a dropdown and its menu behave: who owns what, the keyboard, the pointer, submenus and placement.</summary>
internal sealed partial class UiDropdownBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[Owns(), Placement(), Keyboard(), Pointer(), KeepOpen(), Open(), Rows()];

    private static UiDocsTopic Owns() =>
        UiDocsTopic.Key("owns").Heading("Who owns what").Paragraphs([
            "The browser owns the open state. The menu is a `[popover]` the trigger opens with `popovertarget`, so "
            + "the browser opens and closes it (the top layer, Escape, a click outside) with no runtime at all, and "
            + "it is placed with CSS anchor positioning.",
            "Where the reader is in an open menu is not the page's state either: the runtime keeps the cursor in "
            + "the browser (`data-rask-menu-cursor` on the menu), so a pointer gliding down the rows and an arrow "
            + "key held down send the server nothing (no event, no render, no patch) on either host.",
            "What the page hears is a row being pressed (its `OnClick`, a checkbox's or a radio's value) and, only "
            + "when it gave `OnToggle`, the menu opening and closing. Without `OnToggle`, opening and closing a "
            + "dropdown, a popover or a context menu sends nothing, and neither does a `Ui.MenuItem` with no "
            + "`OnClick`: `data-open` and `aria-expanded` are written in the browser.",
        ]);

    private static UiDocsTopic Placement() =>
        UiDocsTopic.Key("placement").Heading("Placement").Paragraphs([
            "Every panel the kit opens against a trigger stays inside the viewport as Flux's does: a menu, a "
            + "popover, a tooltip, a list of options, a calendar, a list of times. When there is no room where it "
            + "asked to be it takes the other alignment, then the other side, and is slid back to stay 5px inside "
            + "the edge; one taller than what is left is held to that height and scrolls inside. It is the "
            + "runtime's `data-rask-place` hook, with nothing to write in an app.",
            "While a menu is open the page behind does not scroll and does not take the pointer, so a click outside "
            + "only closes it (`data-rask-lock` on the menu, a dropdown's and a context menu's alike, as on Flux), "
            + "and the runtime hands focus back to the trigger after it. A menu the pointer opened, `Hover()` "
            + "(`data-rask-hover`), locks nothing and takes no focus.",
        ]);

    private static UiDocsTopic KeepOpen() =>
        UiDocsTopic.Key("keep-open").Heading("Keep open").Paragraphs([
            "A pick closes the menu, a checkbox or a radio too, as in Flux. `KeepOpen` on the menu, a submenu, a "
            + "radio group or a single row keeps it up, for the menu someone ticks several of.",
        ]);

    private static UiDocsTopic Open() =>
        UiDocsTopic.Key("open").Heading("Open state").Paragraphs([
            "`Open` is nullable, and its three settings mean three things. Unset leaves it to the reader; `true` and "
            + "`false` hand it to the page, and the runtime shows or hides the popover to match whenever the page "
            + "changes its mind, which is what lets a dropdown close itself when the action inside it completes: "
            + "`Ui.Dropdown.Open(_open).OnToggle(open => _open = open)[trigger, Ui.Menu[…]]`.",
        ]);

    private static UiDocsTopic Rows() =>
        UiDocsTopic.Key("rows").Heading("Rows, links and keys").Paragraphs([
            "The first child of a dropdown is the trigger: a `Button`, a `Ui.Button`, any element that is a button. "
            + "The second is what it opens: a `Ui.Menu` of actions, a `Ui.Navmenu` of links, or a `Ui.Popover`.",
            "A `Ui.NavmenuItem` is a link, with no menu roles and no cursor. Without an `Href` it is a `<button>` "
            + "with `OnClick`; a string `Href` is a plain `<a>`, and a generated route navigates in the app.",
            "`Key` can go anywhere in a row's chain, `Ui.MenuCheckbox.Value(x).Key(\"k\")`, and a generic row such "
            + "as `Ui.MenuRadioGroup` takes one too.",
            "Style the rows from `data-active` and `data-checked`, and from `data-open` on the dropdown, its "
            + "trigger and an open submenu.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("Keyboard").Paragraphs([
            "Flux's, key for key. The menu opens with focus on it and no row chosen. The row under the cursor has "
            + "focus (roving focus, not `aria-activedescendant`), `tabindex=\"0\"` and `data-active`, all three "
            + "written by the runtime and held against the next render, never rendered.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("`ArrowDown` on the closed trigger", "Opens the menu onto its first row."),
                ("`ArrowDown` / `ArrowUp`", "The first press puts the cursor on the first row; after that the "
                                            + "cursor moves a row at a time, stops at the ends and steps over "
                                            + "disabled rows."),
                ("A letter", "Jumps to the row that starts with it."),
                ("`ArrowRight` or `Enter` on a submenu's row", "Opens the submenu onto its first row."),
                ("`ArrowLeft` in a submenu", "Closes it, with the cursor back on its row."),
                ("`Enter` or `Space`", "Picks: the runtime presses the focused row, then closes the menu."),
                ("`Tab`", "Leaves the menu, which closes it."),
                ("`Escape`", "Closes it: the browser's own."),
                ("`Home`, `End`, `PageUp`, `PageDown`", "Nothing: they are not menu keys."),
            ])
        ];

    private static UiDocsTopic Pointer() =>
        UiDocsTopic.Key("pointer").Heading("Pointer, and submenus").Paragraphs([
            "There is one highlight. A row is lit by `data-active` and by nothing else, no `:hover`, whether the "
            + "pointer or the keyboard lit it. The runtime writes it, in the browser, the moment the pointer enters a "
            + "row (`data-rask-menu-pointer`). Focus does not move, as on Flux, so the row the keyboard was on keeps "
            + "the tab stop.",
            "The arrows count from the lit row: with focus still on the menu the first arrow takes the row the "
            + "pointer is on, and after that each goes on from whichever row is lit. When the pointer leaves the menu "
            + "the row it lit goes dark, and the keyboard's row stays lit.",
            "A submenu flies out beside its row the moment a pointer rests on it. It stays while the pointer moves "
            + "diagonally toward it, across the rows below, because the triangle between the pointer and the flyout "
            + "still counts as the row (`data-rask-safe-area`, Flux's safe area). It stays after the pointer has left "
            + "the menu altogether, and closes when another row is entered.",
            "While the arrows are inside a flyout its submenu's row stays lit with the row they are on. A tap opens a "
            + "submenu on a touch screen, and the next one closes it. An open flyout is `data-open` on the submenu, "
            + "written by the runtime like the rest.",
        ]);
}
