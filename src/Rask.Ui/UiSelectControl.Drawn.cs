using Rask.Core.Forms;

namespace Rask;

// The two drawn variants: a button (listbox) or a text input (combobox), and the list either opens.
public abstract partial class UiSelectControl<T>
{
    private Component Drawn(UiWithField field, Parts parts)
    {
        var view = Read(field, parts);

        // Every child is keyed: the clear slot comes and goes between the trigger and the list, and the posted
        // values change in number. Keyed, each of those is an insert or a removal the live diff ships. By
        // position the slot would be patched into the list and the pick answered with the whole page.
        List<Component?> children =
        [
            Trigger(view),
            Popup(view),
            // After the list, where it is laid over the button's end all the same: its handler is then numbered
            // after the rows', and a pick that brings the button in leaves every row wired as it was.
            Clears(view) && !IsCombobox && Pills is null ? Div.Key("clear").Class(UiClass.Compose(UiSelectLook.ClearSlot, ButtonHeight(view)))[ClearButton()] : null,
            .. PostedValues(view),
        ];

        return Div.Class(UiClass.Compose(UiSelectLook.Root, Clears(view) ? "relative" : null, Class)).Attributes(RootMarks())[children];
    }

    private Component Trigger(View view)
    {
        if (Pills is not null)
        {
            return PillTrigger(view);
        }

        return IsCombobox ? InputTrigger(view) : ButtonTrigger(view);
    }

    private Dictionary<string, string?> RootMarks() => new(StringComparer.Ordinal)
    {
        ["data-ui-control"] = null,
        [Pills is null ? "data-ui-select" : "data-ui-pillbox"] = null,
        ["data-rask-list"] = ListKind(),
    };

    // What kind of list the browser is keeping (rask-list.ts): its cursor, its keys, whether it is open and
    // which rows a search leaves are all there, and none of it is a render's to say.
    private string ListKind()
    {
        List<string> words = [];
        if (IsMultiple)
        {
            words.Add("multiple");
            if ((Clear ?? Ui.SelectClear.Select) != Ui.SelectClear.Select)
            {
                words.Add("stay");
            }
        }

        if (HidesRows)
        {
            words.Add("filter");
        }

        if (IsCombobox)
        {
            words.Add("strict");
        }

        if (Pills is not null)
        {
            words.Add("top");
        }

        if (PillsCombobox)
        {
            words.Add("clears");
        }

        return string.Join(' ', words);
    }

    private const string TriggerKey = "trigger";

    private bool IsOff(Parts parts) => Disabled == true || parts.Button?.Disabled == true;

    private bool Clears(View view) =>
        (Clearable == true || view.Parts.Button?.Clearable == true || Pills?.Trigger?.Clearable == true)
        && view.Picked.Count > 0 && !IsOff(view.Parts);

    // How tall the listbox's button is, for the clear button laid over its end.
    private string ButtonHeight(View view) =>
        UiSelectLook.ClearHeight(view.Parts.Button?.Size ?? Height);

    // ----- Listbox: the button -------------------------------------------------------------------------

    private Component ButtonTrigger(View view)
    {
        var slot = view.Parts.Button;

        return Button
            .Key(TriggerKey)
            .Id(view.Field.ControlId)
            .Type(ButtonType.Button)
            .Role("combobox")
            .Disabled(IsOff(view.Parts))
            .Class(UiClass.Compose(UiSelectLook.Button, UiSelectLook.ButtonSize(slot?.Size ?? Height), slot?.Class))
            .Aria(TriggerAria(view))
            .Attributes(TriggerMarks(view, "data-ui-select-button", view.Field.Invalid || slot?.Invalid == true, anchors: true))[
            Prefix is { } prefix ? Span.Class(UiSelectLook.Prefix).Data("ui-select-prefix", "")[prefix] : null,
            Div.Class(UiSelectLook.Selected)[PickedContent(view, slot?.Placeholder ?? Placeholder)],
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiSelectLook.Chevron)
        ];
    }

    // What the button says: the placeholder, the one picked option as its row draws it, or a count. One of the
    // three at a time, as Flux stamps them — and each under its own key, so going from one to another is a keyed
    // swap the live diff ships. Unkeyed, a span against a div is a change it answers with the whole page.
    //
    // The answers counted are the value's, not the rows': Flux's "3 selected" stands when a search has taken
    // two of the three out of the list. One answer is said as its row draws it, or as the select last drew it
    // once the row has gone (Remember) — the same key and the same shape, so a render that brings other options
    // changes nothing in the button.
    private Component? PickedContent(View view, string? placeholder)
    {
        var count = view.Picked.Count;
        if (count > 1)
        {
            return Div.Key("count").Class(UiSelectLook.Picked)[
                count.ToString(System.Globalization.CultureInfo.CurrentCulture) + " " + (SelectedSuffix ?? RaskStrings.Get(RaskString.SelectSelectedSuffix, "selected"))
            ];
        }

        if (view.Answer is not { } answer)
        {
            return placeholder is null
                ? null
                : Span.Key("placeholder").Class(UiSelectLook.Placeholder).Data("ui-select-placeholder", "")[placeholder];
        }

        return Div.Key("option:" + answer.FormValue).Class(UiSelectLook.Picked).Data("value", answer.FormValue)[
            view.AnswerRow is { } row ? UiListboxRow.Content(row.Option, row.Option.SelectedLabel, inList: false) : UiListboxRow.Content(answer)
        ];
    }

    // ----- Combobox: the input -------------------------------------------------------------------------

    private Component InputTrigger(View view)
    {
        var slot = view.Parts.Input;
        var size = UiSelectLook.InputSize(slot?.Size ?? Height);

        return Div.Key(TriggerKey).Class(UiInputLook.Root).Attributes(InputBoxMarks())[
            Input
                // The answer's words. What is typed over them is the reader's, and the browser puts the answer
                // back when the list shuts (`strict`, rask-list.ts).
                .Value(PickedWords(view))
                .Id(view.Field.ControlId)
                .Type(InputType.Text)
                .Placeholder(slot?.Placeholder ?? Placeholder)
                .Role("combobox")
                .Autocomplete("off")
                .Disabled(Disabled == true)
                .Class(UiClass.Compose(
                    UiInputLook.Control,
                    UiInputLook.Size(size),
                    UiInputLook.Padding(leading: false, trailing: true),
                    UiInputLook.Variant(Ui.InputVariant.Outline),
                    slot?.Class))
                .Aria(TriggerAria(view, autocompletes: true))
                .Attributes(InputMarks(view, view.Field.Invalid || slot?.Invalid == true)),
            // Keyed, all three: the clear button comes and goes before the chevron, and is no chevron to patch into one.
            Div.Class(UiInputLook.Trailing)[
                Clears(view) ? ClearButton() : null,
                Filter == false ? Ui.Icon.Key("busy").Name(Ui.IconName.Loading).Class(UiSelectLook.InputBusy) : null,
                Button
                    .Key("chevron")
                    .Type(ButtonType.Button)
                    .TabIndex(-1)
                    .Disabled(Disabled == true)
                    .Class(UiSelectLook.InputChevron)
                    .Aria(ChevronAria())
                    .Attributes(ChevronMarks())[
                    Ui.Icon.Name(Ui.IconName.ChevronUpDown).Mini.Class(UiSelectLook.InputChevronIcon)
                ]
            ]
        ];
    }

    // The box is what the list hangs from: the input and its chevron together.
    private Dictionary<string, string?> InputBoxMarks() => new(StringComparer.Ordinal)
    {
        ["data-ui-input"] = null,
        ["style"] = "anchor-name:--" + Prefixed,
    };

    private Dictionary<string, string?> ChevronMarks() => new(StringComparer.Ordinal)
    {
        ["data-ui-button"] = null,
        ["popovertarget"] = PanelId,
    };

    private Dictionary<string, string?> ChevronAria() => new(StringComparer.Ordinal)
    {
        ["controls"] = ListId,
        ["haspopup"] = "listbox",
        ["expanded"] = "false",
    };

    // ----- Shared by both triggers ---------------------------------------------------------------------

    // Rendered shut, always: `aria-expanded`, `data-open` and the row `aria-activedescendant` names are written
    // by the browser as the list opens and its cursor moves.
    private Dictionary<string, string?> TriggerAria(View view, bool autocompletes = false)
    {
        var aria = new Dictionary<string, string?>(view.Field.Aria, StringComparer.Ordinal)
        {
            ["controls"] = ListId,
            ["haspopup"] = "listbox",
            ["expanded"] = "false",
        };
        if (autocompletes)
        {
            aria["autocomplete"] = "list";
        }

        return aria;
    }

    // The combobox's input: the trigger's marks, and what it tells the page of the text typed into it.
    private Dictionary<string, string?> InputMarks(View view, bool invalid)
    {
        var marks = TriggerMarks(view, "data-ui-control", invalid, anchors: false);
        // The words typed narrow the list; the answer is the option picked.
        marks[UiRuntimeMarks.Unguarded] = null;

        return HearingMarks(marks, view.Parts.Create is not null);
    }

    private Dictionary<string, string?> TriggerMarks(View view, string marker, bool invalid, bool anchors)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [marker] = null,
            ["data-ui-group-target"] = null,
        };
        if (anchors)
        {
            // Closed, Enter does not press it and the arrows do not scroll the page: Flux's button, not a native one.
            marks["data-rask-listbox-button"] = null;
            marks["popovertarget"] = PanelId;
            marks["style"] = "anchor-name:--" + Prefixed;
        }

        if (invalid)
        {
            marks["data-invalid"] = null;
        }

        if (view.Picked.Count > 0)
        {
            marks["data-selected"] = null;
        }

        return marks;
    }

    // Out of the tab order, as the input's own clear button is: Backspace and Escape reach the same end.
    private Component ClearButton() =>
        Button
            .Key("clear")
            .Type(ButtonType.Button)
            .TabIndex(-1)
            .Class(UiInputLook.Action)
            .Aria("label", RaskStrings.Get(RaskString.SelectClear, "Clear selected"))
            .Data("ui-button", "")
            .Attributes(UiDraws.Marks(("click", CommitDraws)))
            .OnClick(ClearAsync)[
            Ui.Icon.Name(Ui.IconName.XMark).Mini
        ];

    // The drawn variants post nothing by themselves, so a named select carries its answer in hidden inputs.
    private IEnumerable<Component?> PostedValues(View view) =>
        Name is { } name
            ? Posted(view).DefaultIfEmpty(string.Empty)
                .Select(value => Input.Value(value).Key("posted:" + value).Type(InputType.Hidden).Name(name))
            : [];

    // An answer whose row a render took away is still an answer, and still posted: after the ones among the rows.
    private static IEnumerable<string> Posted(View view) =>
        view.PickedRows.Select(row => row.Option.FormValue)
            .Concat(view.Answers.Where(answer => answer.Row is null).Select(answer => answer.Face.FormValue));
}
