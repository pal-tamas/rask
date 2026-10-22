namespace Rask.Site.Features.UiDocs;

/// <summary>How an autocomplete behaves: its text, suggestions from the server, the keyboard, and the runtime's list.</summary>
internal sealed partial class UiAutocompleteBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Text(),
            Server(),
            Keyboard(),
            NoScript()
        ];

    private static UiDocsTopic Text() =>
        UiDocsTopic.Key("text").Heading("It holds the text").Paragraphs([
            "An item has no value of its own, and what is typed need not be an item. The text reaches the page as an "
            + "input's does: a bound member with the next action (`.Live()`: as it is typed), and `OnChange` when an item "
            + "is picked, when Escape empties the input, and when what was typed is left.",
            "It takes the input's own steps, which is what Flux documents for it: `Type`, `Size`, `Variant`, `Icon`, "
            + "`Kbd`, `Clearable()`, `Copyable()`, `Mask` and the rest, and `ContainerClass` for the open list (a "
            + "height, such as `max-h-80`).",
        ]);

    private static UiDocsTopic Server() =>
        UiDocsTopic.Key("server").Heading("Suggestions from the server").Paragraphs([
            "`Debounce(250.Milliseconds)` hands the page what was typed once typing has paused as well (Flux's "
            + "`wire:model.live.debounce.250ms`), and the page answers it with the items. The autocomplete has no search "
            + "callback apart from its value: what is typed is what it holds, so the pause is when the value arrives.",
            "The list still narrows at every keystroke, since Flux's autocomplete has no `filter` to turn off, and only "
            + "the page waits. An item pressed during the pause is the text, and what was typed before it is never handed "
            + "over. A bound autocomplete writes its member at the pause: `Debounce` is a way of being `Live()`, so "
            + "`AfterBind` is where the search goes.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "Recorded on fluxui.dev and matched. The cursor, the filter and the pick are the browser's, and nothing is "
            + "sent to the page until the input's text is.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("A click, ArrowDown, ArrowUp", "Open the list; the cursor starts on the item last picked, else the first."),
                ("Typing", "Opens the list on the items that hold the text anywhere, whatever the case or the accents. When nothing matches, nothing is drawn."),
                ("ArrowDown, ArrowUp", "Move the cursor and stop at either end. Home and End are the text's own."),
                ("Enter", "Writes the active item into the input and closes. With no active item it does nothing."),
                ("Escape", "Closes the list and empties the input, open or not."),
                ("Tab, a click elsewhere", "Close, and keep whatever was typed."),
                ("The pointer", "Lights the row it enters, which goes dark when the pointer leaves the list; a click picks it and the focus stays in the input."),
            ])
        ];

    private static UiDocsTopic NoScript() =>
        UiDocsTopic.Key("runtime").Heading("No script of the kit's").Paragraphs([
            "The list is a native `popover` placed by CSS anchor positioning, and the cursor, the filter, the open state "
            + "and the pick are the runtime's (`data-rask-list`). The page behind the open list is locked "
            + "(`data-rask-lock`).",
            "The input is `role=\"combobox\"` with `aria-autocomplete=\"list\"`, `aria-haspopup=\"listbox\"`, "
            + "`aria-expanded`, `aria-controls` and, while open, `aria-activedescendant`. The list is `role=\"listbox\"` "
            + "and, as Flux writes it, `aria-multiselectable=\"true\"`; the item last picked stays `aria-selected` "
            + "whatever is typed afterwards.",
        ]);
}
