namespace Rask.Server.E2E.Tests;

public abstract partial class KitDrawsWithoutAskingTests
{
    /// <summary>Frames a gesture owes: a value that changed, or a callback the page gave the component.</summary>
    private static readonly Dictionary<string, (int Frames, string Why)> Sends = new(StringComparer.Ordinal)
    {
        ["autocomplete: pick an item"] = (1, "the value, on change"),
        ["calendar-range: pick the last day of a range"] = (1, "the range, once it is whole: Flux's calendar says nothing of its first day"),
        ["calendar: pick a day"] = (1, "the value"),
        ["checkbox-group: tick one"] = (1, "the value, bound live"),
        ["checkbox: tick"] = (1, "the value"),
        ["color-picker: cursor keys"] = (9, "the colour, a change a key, as Flux's picker announces one for every key of its area"),
        ["color-picker: drag and release"] = (1, "the colour, on letting go: Flux's picker says `change` once, at the release"),
        ["editor: typing"] = (3, "the value, as it is typed: a frame a key"),
        ["editor: typing, then leaving the field"] = (3, "the value, as it is typed: a frame a key, and nothing on leaving"),
        ["date-picker-input: typing a part of the date"] = (2, "the value each time the parts make a new one, as Flux's field says it: 02/15, 02/20"),
        ["date-picker-input: typing a whole date"] = (3, "the value each time the parts make a new one, as Flux's field says it: 02/15, 02/20, then 2027 once its fourth digit is in"),
        ["date-picker: pick a day"] = (1, "the value, and the popup closes in the browser"),
        ["datetime-picker: pick a day"] = (1, "the value: a day picked beside a chosen time is the value at once, the popup open, as Flux's"),
        ["datetime-picker: pick a time"] = (1, "the value, and the popup closes in the browser"),
        ["input-clearable: clear"] = (1, "the value, emptied"),
        ["input: typing, then leaving the field"] = (1, "the value, on change"),
        ["composer: send by Ctrl+Enter"] = (1, "the page's OnSubmit, with what was typed"),
        ["dropdown-heard: open by a press"] = (1, "the page's OnToggle"),
        ["dropdown-heard: close by Escape"] = (1, "the page's OnToggle"),
        ["dropdown-heard: close by a press outside"] = (1, "the page's OnToggle"),
        ["modal-lazy: open by a press"] = (1, "the opening, answered with what a lazy modal holds"),
        ["modal-lazy: close by Escape"] = (1, "the closing: what it holds is taken away again"),
        ["modal-lazy: close by a press outside"] = (1, "the closing: what it holds is taken away again"),
        ["modal-lazy: close by its button"] = (1, "the closing: what it holds is taken away again"),
        ["modal-state: open by the page's own handler"] = (1, "the page's OnClick"),
        ["modal-state: close by Escape"] = (1, "the page's OnClose"),
        ["modal-state: close by its button"] = (1, "the page's OnClose"),
        ["otp: typing half the code"] = (2, "the value, as it is typed: a frame a key"),
        ["otp: typing the whole code"] = (4, "the value, as it is typed: a frame a key"),
        ["pagination: go to another page"] = (1, "the page's OnPage"),
        ["pillbox: pick a row"] = (1, "the value"),
        ["pillbox: take a pill off"] = (1, "the value"),
        ["radio-segmented: choose"] = (1, "the value"),
        ["radio: choose"] = (1, "the value"),
        ["radio: choose by the arrows"] = (2, "the value, once per radio chosen"),
        ["select-combobox: pick a row"] = (1, "the value"),
        ["select-searchable: pick a row"] = (1, "the value"),
        ["select-searched: typing to search"] = (1, "the page's OnInput, once typing has paused"),
        ["select-searched: close by Escape after a search the page heard"] = (1, "the page's OnInput: the search is over"),
        ["select: pick a row"] = (1, "the value"),
        ["slider: arrow keys"] = (5, "the value, as it moves: a frame a key"),
        ["slider: drag and release"] = (12, "the value, as it moves: a frame a step of the pointer, and nothing on letting go"),
        ["slider: drag, still held"] = (12, "the value, as it moves: a frame a step of the pointer"),
        ["toggle-heard: press"] = (1, "the value"),
        ["switch: flip"] = (1, "the value"),
        ["tabs-heard: switch by a press"] = (1, "the page's OnChange"),
        ["tabs-heard: switch by the arrows"] = (3, "the page's OnChange, once per tab chosen"),
        ["textarea: typing, then leaving the field"] = (1, "the value, on change"),
        ["time-picker: pick a time"] = (1, "the page's OnChange"),
        ["time-picker-input: typing a part of the time"] = (1, "the value: the hour typed over 09:30 makes 10:30, as Flux's field says it"),
        ["time-picker-input: typing a whole time"] = (3, "the value each time the parts make a new one, as Flux's field says it: 10:30, 10:04, 10:45"),
        ["toast: raise one from the page's handler"] = (1, "the page's OnClick"),
        ["toast: dismiss it by its button"] = (1, "the toast leaves the queue the server keeps"),
    };

    /// <summary>
    ///     Gestures that only change what is drawn and are still sent, with exactly what each costs today (a pick
    ///     among them costs its one value frame and the rest is drawing). It only shrinks.
    /// </summary>
    private static readonly Dictionary<string, int> Known = new(StringComparer.Ordinal)
    {
        // A select that waits holds its pick for the next action, and the press that makes it is still sent —
        // alone, as drawing (data-rask-draws). Flux draws the picked option itself and sends nothing.
        ["select-bound: pick a row"] = 1,
        ["slider-bound: drag and release"] = 12,
        ["slider-bound: arrow keys"] = 5,
    };

    /// <summary>
    ///     Drawing that is still sent and whose count the BROWSER decides, on a clock that is not the page's: held
    ///     to the most it can cost, and taken out when it costs nothing. Each says why it cannot be exact.
    /// </summary>
    private static readonly Dictionary<string, (int AtMost, string Why)> Unsteady = new(StringComparer.Ordinal);
}
