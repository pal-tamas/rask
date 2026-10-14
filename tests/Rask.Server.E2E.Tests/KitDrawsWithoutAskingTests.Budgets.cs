namespace Rask.Server.E2E.Tests;

public abstract partial class KitDrawsWithoutAskingTests
{
    /// <summary>Frames a gesture owes: a value that changed, or a callback the page gave the component.</summary>
    private static readonly Dictionary<string, (int Frames, string Why)> Sends = new(StringComparer.Ordinal)
    {
        ["calendar: pick a day"] = (1, "the value"),
        ["checkbox-group: tick one"] = (1, "the value, bound live"),
        ["checkbox: tick"] = (1, "the value"),
        ["editor: typing"] = (3, "the value, as it is typed: a frame a key"),
        ["editor: typing, then leaving the field"] = (3, "the value, as it is typed: a frame a key, and nothing on leaving"),
        ["data-grid: sort a column"] = (1, "the rows are sorted on the server"),
        ["filter: filter"] = (1, "the value"),
        ["input-clearable: clear"] = (1, "the value, emptied"),
        ["input: typing, then leaving the field"] = (1, "the value, on change"),
        ["dropdown-heard: open by a press"] = (1, "the page's OnToggle"),
        ["dropdown-heard: close by Escape"] = (1, "the page's OnToggle"),
        ["dropdown-heard: close by a press outside"] = (1, "the page's OnToggle"),
        ["modal-state: open by the page's own handler"] = (1, "the page's OnClick"),
        ["modal-state: close by Escape"] = (1, "the page's OnClose"),
        ["modal-state: close by its button"] = (1, "the page's OnClose"),
        ["otp: typing half the code"] = (2, "the value, as it is typed: a frame a key"),
        ["otp: typing the whole code"] = (4, "the value, as it is typed: a frame a key"),
        ["pagination: go to another page"] = (1, "the page's OnPage"),
        ["radio-segmented: choose"] = (1, "the value"),
        ["radio: choose"] = (1, "the value"),
        ["radio: choose by the arrows"] = (2, "the value, once per radio chosen"),
        ["rating: rate"] = (1, "the value"),
        ["slider: arrow keys"] = (5, "the value, as it moves: a frame a key"),
        ["slider: drag, still held"] = (12, "the value, as it moves: a frame a move of the pointer, twelve moves"),
        ["slider: drag and release"] = (12, "the value, as it moves: a frame a move of the pointer, twelve moves; the release adds none"),
        ["toggle-heard: press"] = (1, "the value"),
        ["switch: flip"] = (1, "the value"),
        ["tabs-heard: switch by a press"] = (1, "the page's OnChange"),
        ["tabs-heard: switch by the arrows"] = (3, "the page's OnChange, once per tab chosen"),
        ["textarea: typing, then leaving the field"] = (1, "the value, on change"),
        ["toast: raise one from the page's handler"] = (1, "the page's OnClick"),
        ["toast: dismiss it by its button"] = (1, "the toast leaves the queue the server keeps"),
    };

    /// <summary>
    ///     Gestures that only change what is drawn and are still sent, with exactly what each costs today (a pick
    ///     among them costs its one value frame and the rest is drawing). It only shrinks.
    /// </summary>
    private static readonly Dictionary<string, int> Known = new(StringComparer.Ordinal)
    {
        ["calendar-range: focus and blur by Tab"] = 5,
        ["calendar-range: pick the first day of a range"] = 1,
        ["calendar-range: pick the last day of a range"] = 2,
        ["calendar-range: pointer over the days while a range is being chosen"] = 24,
        ["calendar: focus and blur by Tab"] = 3,
        ["calendar: page the month"] = 2,
        ["calendar: cursor keys"] = 6,
        ["color-picker: focus and blur by Tab"] = 3,
        ["color-picker: open by a press"] = 1,
        ["color-picker: cursor keys"] = 9,
        ["color-picker: close by Escape"] = 2,
        ["color-picker: close by a press outside"] = 1,
        ["color-picker: drag, still held"] = 12,
        ["color-picker: drag and release"] = 12,
        ["color-picker: typing part of a colour"] = 6,
        ["date-picker-input: focus and blur by Tab"] = 2,
        ["date-picker-input: typing a part of the date"] = 2,
        ["date-picker-input: typing a whole date"] = 3,
        ["date-picker: focus and blur by Tab"] = 3,
        ["date-picker: open by a press"] = 1,
        ["date-picker: close by Escape"] = 1,
        ["date-picker: close by a press outside"] = 1,
        ["date-picker: page the month"] = 2,
        ["date-picker: pick a day"] = 2,
        ["slider-bound: drag and release"] = 12,
        ["slider-bound: arrow keys"] = 5,
        ["time-picker-input: typing a part of the time"] = 2,
        ["time-picker-input: typing a whole time"] = 4,
        ["time-picker: focus and blur by Tab"] = 3,
        ["time-picker: open by a press"] = 1,
        ["time-picker: cursor keys"] = 9,
        ["time-picker: close by Escape"] = 2,
        ["time-picker: close by a press outside"] = 1,
        ["time-picker: pick a time"] = 3,
        ["tree: focus and blur by Tab"] = 3,
        ["tree: cursor keys"] = 9,
        ["tree: press a node"] = 1,
    };

    /// <summary>
    ///     Drawing that is still sent and whose count the BROWSER decides, on a clock that is not the page's: held
    ///     to the most it can cost, and taken out when it costs nothing. Each says why it cannot be exact.
    /// </summary>
    private static readonly Dictionary<string, (int AtMost, string Why)> Unsteady = new(StringComparer.Ordinal)
    {
        // Counted between 12 and 38. A defect of the list's own, seen here: a row lit by the pointer scrolls the list.
        ["time-picker: pointer over the open popup"] = (96,
            "the list snaps to the row it lit, in a frame of the browser's own, which moves another row under the resting "
            + "pointer, which is lit in turn: as many rows as the browser gets through, and the list has 96"),
    };

    /// <summary>
    ///     The select family, which is being moved into the browser on a branch of its own: counted so the table
    ///     is whole, held to what it cost when counted, and not asked to shrink from here.
    /// </summary>
    private static readonly Dictionary<string, int> Elsewhere = new(StringComparer.Ordinal)
    {
        ["autocomplete: focus and blur by Tab"] = 4,
        ["autocomplete: open by a press"] = 1,
        ["autocomplete: pointer over the open popup"] = 3,
        ["autocomplete: cursor keys"] = 3,
        ["autocomplete: close by Escape"] = 2,
        ["autocomplete: close by a press outside"] = 1,
        ["autocomplete: typing to filter"] = 2,
        ["pillbox: focus and blur by Tab"] = 3,
        ["pillbox: open by a press"] = 1,
        ["pillbox: pointer over the open popup"] = 4,
        ["pillbox: cursor keys"] = 9,
        ["pillbox: close by Escape"] = 2,
        ["pillbox: close by a press outside"] = 1,
        ["select-combobox: focus and blur by Tab"] = 5,
        ["select-combobox: open by a press"] = 1,
        ["select-combobox: pointer over the open popup"] = 4,
        ["select-combobox: cursor keys"] = 3,
        ["select-combobox: close by Escape"] = 3,
        ["select-combobox: close by a press outside"] = 2,
        ["select-combobox: typing to filter"] = 2,
        ["select-native: focus and blur by Tab"] = 2,
        ["select-searchable: focus and blur by Tab"] = 5,
        ["select-searchable: open by a press"] = 1,
        ["select-searchable: pointer over the open popup"] = 7,
        ["select-searchable: cursor keys"] = 3,
        ["select-searchable: close by Escape"] = 1,
        ["select-searchable: close by a press outside"] = 1,
        ["select-searchable: typing to filter"] = 2,
        ["select: focus and blur by Tab"] = 5,
        ["select: open by a press"] = 1,
        ["select: pointer over the open popup"] = 4,
        ["select: cursor keys"] = 9,
        ["select: close by Escape"] = 2,
        ["select: close by a press outside"] = 1,
    };
}
