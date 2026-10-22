namespace Rask.Site.Features.UiDocs;

/// <summary>How a pillbox behaves: its collection, searching on the server, the pills that stay, and the keyboard.</summary>
internal sealed partial class UiPillboxBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Value(),
            Server(),
            Pills(),
            Keyboard(),
            NoScript()
        ];

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("A collection of answers").Paragraphs([
            "`Bind` over a `List<T>`, a `T[]` or a `HashSet<T>`, or `Values(…)` with `OnChange`, stands where Flux has "
            + "`wire:model`, and a bound collection gets its own kind back. An option with no `Value` stands for its own "
            + "text; `SelectedLabel` is what its pill says when that is not its words.",
            "Flux's `trigger`, `search`, `input` and `empty` slots are children here: `Ui.PillboxTrigger`, "
            + "`Ui.PillboxSearch`, `Ui.PillboxInput` and `Ui.PillboxOptionEmpty`. `Ui.PillboxOptionCreate` is offered "
            + "once the typed text is `MinLength` long and names no option; with nothing to type into it is always "
            + "there. Written before the options, it is drawn before them.",
        ]);

    private static UiDocsTopic Server() =>
        UiDocsTopic.Key("server").Heading("Searching on the server").Paragraphs([
            "`Filter(false)` leaves the options to the page, and `Debounce` on the input among the pills, or on "
            + "`Ui.PillboxSearch` over the list, asks it once typing has paused. Nothing is sent while the reader types "
            + "into the open list. A row pressed, or Enter, during the pause adds the pill from the list as drawn, and "
            + "the text typed since is dropped.",
            "A pick, a pill taken off and the list shutting each empty what was typed, and `OnInput` then hears `\"\"` "
            + "when the page had heard a search, as Flux's `search` model does.",
        ]);

    private static UiDocsTopic Pills() =>
        UiDocsTopic.Key("pills").Heading("The pills stay when the rows change").Paragraphs([
            "A search the page answers may bring rows that no longer hold a picked option. The pillbox keeps what each "
            + "answer showed when it was last drawn, for as long as the value holds it: the pills stand in the order "
            + "picked, the cross on one still takes it out of the value, and Backspace still takes the last. A value no "
            + "render ever drew an option for has no pill: render its option once, or keep it among the rows the search "
            + "answers with, as the backend example does.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "It is the list a select's listbox draws, under another trigger, so its keys are the runtime's too. Where "
            + "they differ from the select's, as recorded on Flux's page:",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("A click, Space, ArrowDown, ArrowUp", "Open the list. Enter on the closed trigger does nothing."),
                ("Enter, a click on a row", "Switch the row; the list stays open. Pills stand in the order they were picked."),
                ("A letter on the closed trigger", "Switches the next option that starts with it, without opening."),
                ("The cross on a pill", "Takes that option off; the list stays as it was, and what was typed among the pills is emptied."),
                ("Searchable", "The search field takes the focus; a pick empties it and sends the cursor back to the top."),
                ("Combobox", "Typing opens and narrows the list; a pick empties the input; Backspace in the empty input takes the last pill off; Escape and Tab close and empty it."),
            ])
        ];

    private static UiDocsTopic NoScript() =>
        UiDocsTopic.Key("runtime").Heading("No script of the kit's").Paragraphs([
            "The page behind an open list is locked (`data-rask-lock`), and the trigger keeps the keys Flux's keeps "
            + "(`data-rask-contain-keys`), so opening it from the keyboard does not scroll the page. An open pillbox sends "
            + "the page nothing until a row or a cross is pressed.",
            "The trigger is `role=\"combobox\"` while there is nothing to type into, and `role=\"button\"` over a search "
            + "field or an input, which is then the combobox; the list is `role=\"listbox\"` with "
            + "`aria-multiselectable=\"true\"`. Flux's `modal` prop has no step: the create row's `OnClick` opens the "
            + "page's own modal.",
        ]);
}
