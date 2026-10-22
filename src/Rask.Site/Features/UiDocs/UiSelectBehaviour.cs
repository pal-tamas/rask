namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How a select behaves: its value and options, searching in the browser or on the server, several answers, the
///     keyboard, and what the browser and the runtime do in place of Flux's script.
/// </summary>
internal sealed partial class UiSelectBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Options(),
            Search(),
            Server(),
            Several(),
            Keyboard(),
            NoScript()
        ];

    private static UiDocsTopic Options() =>
        UiDocsTopic.Key("options").Heading("Options are children").Paragraphs([
            "`Bind` or `Value` stands where Flux has `wire:model`, and the options are the select's children, typed by "
            + "its value: `Ui.Select.Bind(() => m.Country)[countries.Select(c => Ui.SelectOption.Key(c.Code).Value(c.Code)[c.Name])]`. "
            + "An option with no `Value` stands for its own text. Options made from a list take a `Key`.",
            "The native variant is the browser's `<select>`: it needs no runtime, renders complete on a prerendered page "
            + "and gets a phone's own picker, and its options hold text and nothing else. A listbox's button keeps the "
            + "chosen option's words, icon and avatar for as long as the value is that one, whatever rows are drawn, "
            + "and `SelectedLabel` is what it shows when that should not be the option's own words. An `Avatar` wins "
            + "over an `Icon`. `Position` and `Align` say which side the list opens on and which edge it lines up with.",
        ]);

    private static UiDocsTopic Search() =>
        UiDocsTopic.Key("search").Heading("Searching in the browser").Paragraphs([
            "`Searchable()` puts a search field at the top of the listbox's options. Opening focuses it; typing filters "
            + "case- and accent-insensitively over each option's text and its `Keywords`, and puts the cursor on the "
            + "first match. \"No results found\" shows when nothing matches, and `Empty(\"…\")` says it in your own "
            + "words. The filtering is the browser's, as Flux's is: a key typed sends the page nothing.",
            "In a combobox a click or typing opens the list, Enter picks and the input shows the answer. Escape closes "
            + "the list and a second Escape empties the text but keeps the answer; leaving the input with words that "
            + "name nothing puts the answer's text back.",
        ]);

    private static UiDocsTopic Server() =>
        UiDocsTopic.Key("server").Heading("Searching on the server").Paragraphs([
            "`Filter(false)` hands the search to the page: the `OnInput` of `Ui.SelectSearch` or `Ui.SelectInput` hears "
            + "what is typed, at every key or at the pause `Debounce(250.Milliseconds)` names, and the page renders the "
            + "rows that match. The browser counts the pause, so nothing is sent while the reader types. The rows drawn "
            + "stay until the new ones are here, and a row pressed (or Enter) during the pause picks from the list as "
            + "it is drawn; the text typed since is dropped with it.",
            "The page's search follows the field. When the select rewrites the field itself (the list shuts and the "
            + "field is emptied, or a combobox's input says the answer), `OnInput` hears that text too, once, and only "
            + "when the page thinks otherwise: after a search for \"ber\" and a pick it is called with `\"\"`, so the page "
            + "goes back to the rows it starts from.",
            "`Ui.SelectOptionCreate` offers to make an option of what was typed once the search is `MinLength` long and "
            + "names none that exists; its `OnClick` is handed the text, and the page adds the option and chooses it. In "
            + "a listbox with no search it is always shown and handed an empty string: the case where the page opens a "
            + "form of its own. `Ui.SelectOptionEmpty` says what the list says when there are no rows, and `WhenLoading` "
            + "what it says while results are on their way.",
        ]);

    private static UiDocsTopic Several() =>
        UiDocsTopic.Key("several").Heading("Several answers").Paragraphs([
            "Open the select on a collection (`Bind` over a `List<T>`, a `T[]` or a `HashSet<T>`, or `Values(tags)` with "
            + "`OnChange`) and `.Listbox.Multiple()` picks several. A pick switches its row and leaves the list open; the "
            + "button names the one option picked, or counts them (\"3 selected\", or your `SelectedSuffix`). A bound "
            + "list, array or set each gets its own kind back. Several answers are the listbox's, as on Flux: "
            + "`Multiple()` over one value, or a collection on the native variant, throws and says so.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "The keys are the runtime's (`data-rask-list`), recorded on fluxui.dev and matched, and an open list sends the "
            + "page nothing until a row is picked.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("Click, Space, ArrowDown, ArrowUp", "Open, with the cursor on the chosen option, or the first when there is none."),
                ("ArrowDown, ArrowUp", "Move the cursor past a disabled option. They stop at either end; there is no wrap."),
                ("Home, End, PageUp, PageDown", "Nothing, as on Flux."),
                ("Enter", "Picks the lit row and closes; with `Multiple()` it switches the row and the list stays open. On the closed button it does nothing."),
                ("Escape, a click elsewhere", "Close; the focus returns to the button."),
                ("Tab", "Closes, picks nothing, and moves on."),
                ("A letter", "On the closed button it picks the next option starting with it; on the open list it moves the cursor. Letters within half a second are one prefix."),
                ("The pointer", "Lights the row it enters; a row it lit goes dark when it leaves the list, and one the keys lit stays."),
            ])
        ];

    private static UiDocsTopic NoScript() =>
        UiDocsTopic.Key("runtime").Heading("No script of the kit's").Paragraphs([
            "The drawn list is a native `popover` placed by CSS anchor positioning: as wide as its trigger, 5px under "
            + "it, at most 20rem tall, and kept inside the viewport. The browser opens it on a click and closes it on "
            + "Escape or a click elsewhere. While it is open the page behind it neither scrolls nor takes the pointer "
            + "(`data-rask-lock`), and the closed button ignores Enter and its arrows do not scroll the page "
            + "(`data-rask-listbox-button`), as Flux's script does.",
            "The trigger carries `role=\"combobox\"`, `aria-haspopup=\"listbox\"`, `aria-expanded`, `aria-controls` and, "
            + "while open, `aria-activedescendant`; the list is `role=\"listbox\"` and each row `role=\"option\"` with "
            + "`aria-selected`. A drawn select posts nothing by itself, so one with a `Name` carries its answer in hidden "
            + "inputs. A press on a row names its own row, and a bound select then writes its member with the next "
            + "action, or at once with `.Live()`.",
        ]);
}
