using System.Globalization;

namespace Rask;

// The open list: a native popover anchored to the trigger, holding the search field and the rows.
public abstract partial class UiSelectControl<T>
{
    private Component Popup(View view)
    {
        var popup = Div
            .Key("list")
            .Id(PanelId)
            .Popover(Popover.Auto)
            .Class(UiClass.Compose(
                HasSearchField ? UiListboxLook.PopupOverList : UiListboxLook.Popup,
                HasSearchField ? UiListboxLook.Column : UiListboxLook.Scrolls,
                PillPopupLook(),
                OptionsClass))
            .Attributes(PopupMarks());

        return HasSearchField
            ? popup[SearchField(view), ListRole(Div.Id(ListId).Class(UiClass.Compose(UiSelectLook.SearchedList, PillListLook())))[Rows(view)]]
            : ListRole(popup)[Rows(view)];
    }

    // On every list, one answer or several: Flux's listbox says so whatever it holds.
    private static HTMLDivElement ListRole(HTMLDivElement list) =>
        list.Role("listbox").TabIndex(-1).Aria("multiselectable", "true");

    // Opened and shut in the browser, by the trigger and the keys (rask-list.ts): the page says nothing of it,
    // and hears nothing.
    private Dictionary<string, string?> PopupMarks() => new(StringComparer.Ordinal)
    {
        [Pills is null || HasSearchField ? "data-ui-options" : "data-ui-listbox-options"] = null,
        // While the list is open the page behind it neither scrolls nor takes the pointer, as on Flux.
        ["data-rask-lock"] = null,
        ["style"] = UiAnchor.Under(Prefixed, Position, Align),
        ["data-rask-place"] = UiAnchor.Place(Position, Align),
    };

    // Every row the control writes is keyed — the options by their own key or their place, the rest by name — so
    // an option created while the list is open is a keyed change the live diff ships. Only a list that holds
    // something of the page's own (a separator) is still walked by position.
    private IEnumerable<Component?> Rows(View view)
    {
        if (Searches)
        {
            yield return EmptyRow(view);
        }

        // Written before the options, drawn before them: the pillbox's "Create new" that opens a form.
        if (view.Parts.Create is { } leading && view.Parts.CreateLeads)
        {
            yield return CreateRow(view, leading);
        }

        foreach (var block in view.Parts.Blocks)
        {
            if (block.Other is { } other)
            {
                yield return other;
                continue;
            }

            if (block.Group is { } group && block.Rows.Count > 0)
            {
                yield return GroupHeading(group, block);
            }

            foreach (var row in block.Rows)
            {
                yield return OptionRow(view, row);
            }
        }

        if (view.Parts.Create is { } create && !view.Parts.CreateLeads)
        {
            yield return CreateRow(view, create);
        }
    }

    // A heading says how many rows it is over: the browser hides it with the last of them a search leaves out.
    private static Component GroupHeading(UiSelectGroup group, Block block) =>
        Div.Key("ui:group:" + block.Rows[0].Index.ToString(CultureInfo.InvariantCulture))
            .Class(UiListboxLook.GroupHeading)
            .Role("presentation")
            .Data("rask-list-group", block.Rows.Count.ToString(CultureInfo.InvariantCulture))[group.Label];

    private Component OptionRow(View view, Row row)
    {
        var off = row.Option.Disabled == true;
        var picked = view.IsPicked(row);
        var item = Div
            .Key(row.Option.Key ?? "ui:option:" + row.Index.ToString(CultureInfo.InvariantCulture))
            .Id(UiSelectNav.OptId(Prefixed, row.Index))
            .Role("option")
            .Class(UiClass.Compose(RowLook(row.Option), row.Option.Class))
            .Aria(UiOptionAria.For(off, picked))
            .Attributes(OptionMarks(row.Option, picked));

        return (off ? item : item.OnClick(() => PickAsync(row.Index, view)))[
            RowContent(row.Option)
        ];
    }

    private string RowLook(UiSelectOption option)
    {
        if (Pills is not null)
        {
            return UiPillboxLook.Option;
        }

        return UiClass.Compose(UiListboxLook.Option, option.Description is null ? null : UiListboxLook.OptionTall);
    }

    // What the browser reads a row by (rask-list.ts). Its words are its text unless the row draws more than
    // them — a description, an avatar's initials, content of the page's own — and then they are said.
    private Dictionary<string, string?> OptionMarks(UiSelectOption option, bool picked)
    {
        var marks = RowMarks(Pills is null ? "data-ui-option" : "data-ui-listbox-option", picked, hidden: false);
        if (option.Description is not null || option.Avatar is not null || option.HasCustomContent)
        {
            marks["data-rask-list-text"] = option.Text;
        }

        if (option.Keywords is { } keywords)
        {
            marks["data-rask-list-keywords"] = keywords;
        }

        if (option.Filterable == false)
        {
            marks["data-rask-list-keep"] = null;
        }

        if (IsCombobox && option.SelectedLabel is { } says)
        {
            marks["data-rask-list-says"] = says;
        }

        return marks;
    }

    private static Dictionary<string, string?> RowMarks(string marker, bool picked, bool hidden)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { [marker] = null };
        if (picked)
        {
            marks["data-selected"] = null;
        }

        if (hidden)
        {
            marks["data-hidden"] = null;
        }

        return marks;
    }

    // There all along and hidden while anything else is: the list never jumps a round trip behind the search.
    // Where the browser hides the rows it shows this one too, once a search has left none; what is rendered is
    // the list before anything is typed.
    private Component EmptyRow(View view)
    {
        var slot = view.Parts.Empty;
        var others = view.Parts.Rows.Count > 0 || (view.Parts.Create is not null && !HidesRows && view.CreateShown);
        var marks = RowMarks("data-ui-listbox-empty", picked: false, hidden: others);
        marks["data-rask-list-empty"] = null;

        return Div.Key("ui:empty").Class(UiClass.Compose(UiListboxLook.Empty, slot?.Class)).Attributes(marks)[
            _busy ? [slot?.WhenLoading ?? RaskStrings.Get(RaskString.SelectLoading, "Loading...")] : slot?.Children ?? [Empty ?? RaskStrings.Get(RaskString.SelectEmpty, "No results found")]
        ];
    }

    // Offered by the browser, at every key, once what is typed is long enough and names no option; a list with
    // nothing to type into always has it. A list the page filters is told by the page.
    private Component CreateRow(View view, UiSelectOptionCreate create)
    {
        var index = view.CreateIndex;
        var marks = RowMarks("data-ui-option-create", picked: false, hidden: Searches && (HidesRows || !view.CreateShown));
        marks["data-rask-list-create"] = Searches ? Math.Max(create.MinLength ?? 1, 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
        if (_busy)
        {
            marks["data-loading"] = "true";
        }

        return Div
            .Key("ui:create")
            .Id(UiSelectNav.OptId(Prefixed, index))
            .Class(UiClass.Compose(UiListboxLook.Create, create.Class))
            .Attributes(marks)
            .OnClick(() => PickAsync(index, view))[
            Div.Class(UiListboxLook.CreateLead)[Ui.Icon.Name(Ui.IconName.Plus).Mini],
            Span.Class(Pills is null ? UiListboxLook.CreateWords : null)[create.Children ?? []],
            Ui.Icon.Name(Ui.IconName.Loading).Class(UiListboxLook.CreateBusy)
        ];
    }

    // ----- The search field over a listbox -------------------------------------------------------------

    private Component SearchField(View view)
    {
        var slot = view.Parts.Search;
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Pills is not null)
        {
            // The pillbox's search field names its list; the select's does not.
            aria["autocomplete"] = "list";
            aria["controls"] = ListId;
        }

        var marks = HearingMarks(slot?.OnInput ?? default, slot?.Pause ?? 0, view.Parts.Create is not null);
        foreach (var (name, value) in SearchInputMarks())
        {
            marks[name] = value;
        }

        return Div.Class(UiClass.Compose(UiSelectLook.Search, slot?.Class)).Data(Pills is null ? "ui-select-search" : "ui-pillbox-search", "")[
            Div.Class(UiSelectLook.SearchIcon)[Ui.Icon.Name(slot?.Icon ?? Ui.IconName.MagnifyingGlass).Micro],
            Input
                // Only what the page itself holds: what is typed is the reader's, and a render says nothing of it.
                .Value(slot?.Value ?? string.Empty)
                .Id(SearchId)
                .Type(InputType.Text)
                // A space when there is none of its own: the clear button reads :placeholder-shown.
                .Placeholder(slot?.Placeholder ?? RaskStrings.Get(RaskString.SelectSearchPlaceholder, "Search..."))
                .Role("combobox")
                .Autocomplete("off")
                .Autofocus()
                .Attributes(marks)
                // The list's keys are the field's while it is typed into: Enter picks instead of submitting a
                // form around the select, and the arrows walk the rows instead of the caret.
                .Data("rask-contain-keys", UiListboxLook.ListKeys)
                .Class(UiSelectLook.SearchInput)
                .Aria(aria),
            slot?.Clearable == false ? null : Div.Class(UiSelectLook.SearchClear)[SearchClear(slot)]
        ];
    }

    // Emptied in the browser (`data-rask-clear`), which tells the list — and the page, where it asked to hear
    // every key. A page that waits for a pause is told by the press itself: clearing is no typing to wait out.
    private Component SearchClear(UiSelectSearch? slot)
    {
        var button = Button
            .Type(ButtonType.Button)
            .TabIndex(-1)
            .Class(UiSelectLook.SearchClearButton)
            .Aria("label", RaskStrings.Get(RaskString.SelectSearchClear, "Clear command input"))
            .Attributes(("data-ui-button", null), ("data-rask-clear", SearchId));

        return (_waits ? button.OnClick(() => HearAsync(string.Empty, slot?.OnInput ?? default)) : button)[
            Ui.Icon.Name(Ui.IconName.XMark).Micro
        ];
    }
}
