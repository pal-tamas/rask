using Rask.Core;

namespace Rask;

// Ui.Pillbox: the same list under a trigger that shows every picked option as a pill. What differs from the
// select is here — the trigger, the pills, the input among them — and is what was recorded on Flux's page:
// Space opens it, a letter typed on the closed trigger switches the next option that starts with it,
// Backspace in the empty input takes the last pill off, and a pick empties what was typed.
public abstract partial class UiSelectControl<T>
{
    private long? _dismissed;

    /// <summary>The pillbox this control is the list of, when it is one's.</summary>
    internal UiPillboxFace? Pills { get; private set; }

    private bool PillsCombobox => Pills?.Combobox == true;

    /// <summary>Makes this control a pillbox's. The kit's own chain step, for <c>Ui.Pillbox</c>.</summary>
    /// <param name="face">What the pillbox draws that the select does not.</param>
    internal UiSelectControl<T> AsPillbox(UiPillboxFace face)
    {
        Pills = face;
        // What a generated step does when it writes a new value: the control is drawn again with it.
        BuilderRuntime.MarkChanged(this);

        return this;
    }

    private Component PillTrigger(View view)
    {
        var slot = Pills!.Trigger;
        var off = IsOff(view.Parts);
        // With nothing to type into, the trigger is the combobox; otherwise it is the button over one.
        var plain = !Searches;
        var pills = PillRows(view);
        var trigger = Div
            .TabIndex(PillsCombobox || off ? -1 : 0)
            .Role(plain ? "combobox" : "button")
            .Class(UiClass.Compose(UiPillboxLook.Trigger, UiPillboxLook.TriggerSize(IsSmall(slot)), PillsCombobox ? UiPillboxLook.TriggerWithInput : "cursor-default", slot?.Class))
            .Aria(PillTriggerAria(view, plain))
            .Attributes(PillTriggerMarks(view, view.Field.Invalid || slot?.Invalid == true || view.Parts.Input?.Invalid == true, off, plain));
        if (!PillsCombobox)
        {
            trigger = trigger.Id(view.Field.ControlId);
        }

        if (!off)
        {
            trigger = trigger.OnClick(OnPillTriggerClickAsync).OnKeyDown(e => OnButtonKeyAsync(e, view));
        }

        return trigger[
            Div.Class(UiPillboxLook.Selected)[
                PillsCombobox ? null : Div.Class(UiPillboxLook.Contents)[PillPlaceholder(pills.Count, slot?.Placeholder ?? Placeholder)],
                Div.Class(UiPillboxLook.Pills)[
                    Div.Class(UiPillboxLook.Contents)[
                        pills.Count > 0 ? Div.Class(UiPillboxLook.Contents)[pills.Select(row => Pill(view, row, off))] : null
                    ],
                    PillsCombobox ? PillInput(view, pills.Count, off) : null
                ]
            ],
            Clears(view) ? ClearButton() : null,
            PillsCombobox
                ? Button.Type(ButtonType.Button).TabIndex(-1).Class(UiPillboxLook.InputChevron).Data("ui-button", "")[
                    Ui.Icon.Name(Ui.IconName.ChevronUpDown).Mini.Class(UiPillboxLook.InputChevronIcon)
                ]
                : Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiClass.Compose(UiPillboxLook.Chevron, UiPillboxLook.ChevronSize(IsSmall(slot))))
        ];
    }

    private bool IsSmall(UiPillboxTrigger? slot) =>
        slot?.Size is { } size ? size == Ui.PillboxSize.Sm : Height == Ui.SelectSize.Sm;

    // In the order they were picked, as Flux shows them — not the order the options are written in.
    private static List<Row> PillRows(View view)
    {
        List<Row> rows = [];
        foreach (var value in view.Picked)
        {
            if (view.Parts.Rows.Find(row => Same(row.Value, value)) is { } row)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private static Component? PillPlaceholder(int pills, string? placeholder) =>
        pills == 0 && placeholder is not null
            ? Span.Class(UiPillboxLook.Placeholder).Data("ui-pillbox-placeholder", "")[placeholder]
            : null;

    private Component Pill(View view, Row row, bool off)
    {
        var small = IsSmall(Pills!.Trigger);
        var remove = Div.Class(UiClass.Compose(UiPillboxLook.Remove, UiPillboxLook.RemoveSize(small)));

        // The option's words, never its content: Flux's pill is text, whatever the row draws beside it.
        return Div.Key(row.Option.FormValue).Class(UiClass.Compose(UiPillboxLook.Pill, UiPillboxLook.PillSize(small))).Data("value", row.Option.FormValue)[
            Div.Class(UiPillboxLook.PillWords)[row.Option.SelectedText],
            (off ? remove : remove.OnClick(() => RemoveAsync(row.Value, view)))[Ui.Icon.Name(Ui.IconName.XMark).Micro]
        ];
    }

    private HTMLInputElement<string> PillInput(View view, int pills, bool off)
    {
        var slot = view.Parts.Input;
        var placeholder = slot?.Placeholder ?? Placeholder;
        var aria = new Dictionary<string, string?>(view.Field.Aria, StringComparer.Ordinal)
        {
            ["autocomplete"] = "list",
            ["controls"] = ListId,
        };
        if (_open && view.Cursor >= 0)
        {
            aria["activedescendant"] = UiSelectNav.OptId(Prefixed, view.Cursor);
        }

        var input = Input
            .Value(view.Search)
            .Id(view.Field.ControlId)
            .Type(InputType.Text);
        if (_open)
        {
            // While the list is open its keys are the input's: Enter picks, the arrows walk the rows.
            input = input.Data("rask-contain-keys", UiListboxLook.ListKeys);
        }

        return input
            // The placeholder is the pillbox's while it holds nothing; Flux keeps it beside the pills as data.
            .Placeholder(pills == 0 ? placeholder : string.Empty)
            .Role("combobox")
            .Disabled(off)
            .Class(UiClass.Compose(UiPillboxLook.Input, slot?.Class))
            .Aria(aria)
            .Attributes(UiPillboxLook.InputMarks(placeholder))
            .Ref(_input)
            .OnInput(text => SearchedAsync(text, slot?.OnInput ?? default, opens: true))
            .OnKeyDown(e => OnPillInputKeyAsync(e, view));
    }

    private Dictionary<string, string?> PillTriggerAria(View view, bool plain)
    {
        // The input is the field's control where there is one; the trigger is, everywhere else.
        var aria = PillsCombobox
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(view.Field.Aria, StringComparer.Ordinal);
        if (plain)
        {
            aria["controls"] = ListId;
            aria["autocomplete"] = "none";
        }

        aria["haspopup"] = "listbox";
        aria["expanded"] = _open ? "true" : "false";
        if (plain && _open && view.Cursor >= 0)
        {
            aria["activedescendant"] = UiSelectNav.OptId(Prefixed, view.Cursor);
        }

        return aria;
    }

    private Dictionary<string, string?> PillTriggerMarks(View view, bool invalid, bool off, bool plain)
    {
        var marks = TriggerMarks(view, "data-ui-pillbox-trigger", invalid, anchors: false);
        marks["style"] = "anchor-name:--" + Prefixed;
        // Flux's trigger opens on these and the page behind it does not move; as a button over a search field it
        // lets Enter through.
        marks["data-rask-contain-keys"] = plain ? "Enter Space ArrowUp ArrowDown" : "Space ArrowUp ArrowDown";
        if (off)
        {
            marks["data-disabled"] = null;
        }

        return marks;
    }

    private string? PillPopupLook() => (Pills, HasSearchField) switch
    {
        (null, _) => null,
        (_, true) => UiPillboxLook.SearchedPopup,
        _ => UiPillboxLook.Popup,
    };

    private string? PillListLook() => Pills is null ? null : UiPillboxLook.SearchedList;

    private Dictionary<string, string?> SearchInputMarks() =>
        Pills is null ? [] : UiPillboxLook.InputMarks(null);

    private Component?[] RowContent(UiSelectOption option) =>
        Pills is null ? [UiListboxRow.Content(option)] : UiPillboxRow.Content(option);

    // ----- What the pointer and the keyboard do --------------------------------------------------------

    private async Task OnPillTriggerClickAsync()
    {
        if (PillsCombobox)
        {
            _open = true;
            await UiInputReach.ReachAsync(() => _input.Focus()).ConfigureAwait(false);
            return;
        }

        // The browser shuts a popover on a click outside it, the trigger included. When it said so first,
        // this is the click that closed the list, not one that opens it.
        if (_dismissed is { } at && Clock.GetElapsedTime(at) < UiPillboxFace.DismissedBy)
        {
            _dismissed = null;
            return;
        }

        _open = !_open;
        _cursor = Unset;
    }

    private async Task OnPopupToggleAsync(ToggleEvent e)
    {
        OnToggle(e);
        if (PillsCombobox && !_open)
        {
            // Flux's `clear="close"`: a closed pillbox keeps nothing of what was typed.
            await UiInputReach.SayAsync(_input, string.Empty).ConfigureAwait(false);
        }
    }

    private async Task OnPillInputKeyAsync(KeyboardEvent e, View view)
    {
        switch (e.Key)
        {
            case Keys.Backspace when view.Search.Length == 0 && view.Picked.Count > 0:
                await CommitAsync([.. view.Picked.Take(view.Picked.Count - 1)]).ConfigureAwait(false);
                break;
            case Keys.Escape or Keys.Tab:
                // The browser emptied the input at the key (UiListKeys.PillsClear): emptied from here, a round
                // trip later, it lost what was typed since.
                Close();
                await ForgetTypedAsync(view, emptied: true).ConfigureAwait(false);
                break;
            case Keys.ArrowDown or Keys.ArrowUp when !_open:
                _open = true;
                _cursor = Unset;
                break;
            default:
                if (_open)
                {
                    await OnListKeyAsync(e, view).ConfigureAwait(false);
                }

                break;
        }
    }

    // Empties the pillbox's own input — unless the browser already has — and tells the page that holds the search.
    private async Task ForgetTypedAsync(View view, bool emptied = false)
    {
        if (view.Search.Length == 0)
        {
            return;
        }

        var cursor = _cursor;
        await SearchedAsync(string.Empty, SearchHeard(view), opens: false).ConfigureAwait(false);
        _typing = false;
        _cursor = cursor;
        if (!emptied)
        {
            await UiInputReach.SayAsync(_input, string.Empty).ConfigureAwait(false);
        }
    }

    private Task RemoveAsync(T value, View view) =>
        CommitAsync([.. view.Picked.Where(held => !Same(held, value))]);
}
