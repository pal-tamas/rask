using Rask.Core.Forms;

namespace Rask;

// The open list: a native popover anchored to the trigger, holding the search field and the rows.
public abstract partial class UiSelectControl<T>
{
    private Component Popup(View view)
    {
        var popup = Div
            .Id(PanelId)
            .Popover(Popover.Auto)
            .Class(UiClass.Compose(UiListboxLook.Popup, HasSearchField ? UiListboxLook.Column : UiListboxLook.Scrolls, PillPopupLook(), OptionsClass))
            .Attributes(PopupMarks())
            .OnToggle(OnPopupToggleAsync);

        return HasSearchField
            ? popup[SearchField(view), ListRole(Div.Id(ListId).Class(UiClass.Compose(UiSelectLook.SearchedList, PillListLook())))[Rows(view)]]
            : ListRole(popup)[Rows(view)];
    }

    private HTMLDivElement ListRole(HTMLDivElement list)
    {
        list = list.Role("listbox").TabIndex(-1);
        if (Pills is not null)
        {
            // Flux's pillbox lights no row once the pointer has left its list.
            list = list.OnMouseLeave(() => _cursor = None);
        }

        // On every list, one answer or several: Flux's listbox says so whatever it holds.
        return list.Aria("multiselectable", "true");
    }

    // `data-rask-popover-open` is how C# opens and closes a popover: the runtime shows or hides it when the
    // value changes. A click on the button and Escape are the browser's, and reach here as the toggle event.
    private Dictionary<string, string?> PopupMarks()
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Pills is null || HasSearchField ? "data-ui-options" : "data-ui-listbox-options"] = null,
            ["data-rask-popover-open"] = _open ? "true" : "false",
            // While the list is open the page behind it neither scrolls nor takes the pointer, as on Flux.
            ["data-rask-lock"] = null,
            ["style"] = UiAnchor.Under(Prefixed, Position, Align),
        };
        if (_open)
        {
            marks["data-open"] = null;
        }

        return marks;
    }

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

            if (block.Group is { } group && block.Rows.Any(row => view.Shown[row.Index]))
            {
                yield return Div.Class(UiListboxLook.GroupHeading).Role("presentation")[group.Label];
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

    private Component OptionRow(View view, Row row)
    {
        var off = row.Option.Disabled == true;
        var picked = view.IsPicked(row);
        var item = Div
            .Key(row.Option.Key)
            .Id(UiSelectNav.OptId(Prefixed, row.Index))
            .Role("option")
            .Class(UiClass.Compose(RowLook(row.Option), row.Option.Class))
            .Aria(UiOptionAria.For(off, picked))
            .Attributes(RowMarks(Pills is null ? "data-ui-option" : "data-ui-listbox-option", picked, view.Cursor == row.Index, !view.Shown[row.Index]));

        return (off ? item : item.OnClick(e => ClickedAsync(e, row.Index, view)).OnMouseEnter(e => Hovered(e, row.Index)))[
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

    private static Dictionary<string, string?> RowMarks(string marker, bool picked, bool active, bool hidden)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { [marker] = null };
        if (picked)
        {
            marks["data-selected"] = null;
        }

        if (active)
        {
            marks["data-active"] = null;
        }

        if (hidden)
        {
            marks["data-hidden"] = null;
        }

        return marks;
    }

    // There all along and hidden while anything else is: the list never jumps a round trip behind the search.
    private Component EmptyRow(View view)
    {
        var slot = view.Parts.Empty;
        var marks = RowMarks("data-ui-listbox-empty", picked: false, active: false, hidden: view.AnyShown || view.CreateShown);

        return Div.Class(UiClass.Compose(UiListboxLook.Empty, slot?.Class)).Attributes(marks)[
            _busy ? [slot?.WhenLoading ?? RaskStrings.Get(RaskString.SelectLoading, "Loading...")] : slot?.Children ?? [Empty ?? RaskStrings.Get(RaskString.SelectEmpty, "No results found")]
        ];
    }

    private Component CreateRow(View view, UiSelectOptionCreate create)
    {
        var index = view.CreateIndex;
        var marks = RowMarks("data-ui-option-create", picked: false, view.Cursor == index, !view.CreateShown);
        if (_busy)
        {
            marks["data-loading"] = "true";
        }

        return Div
            .Id(UiSelectNav.OptId(Prefixed, index))
            .Class(UiClass.Compose(UiListboxLook.Create, create.Class))
            .Attributes(marks)
            .OnClick(e => ClickedAsync(e, index, view))
            .OnMouseEnter(e => Hovered(e, index))[
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

        if (_open && view.Cursor >= 0)
        {
            aria["activedescendant"] = UiSelectNav.OptId(Prefixed, view.Cursor);
        }

        return Div.Class(UiClass.Compose(UiSelectLook.Search, slot?.Class)).Data(Pills is null ? "ui-select-search" : "ui-pillbox-search", "")[
            Div.Class(UiSelectLook.SearchIcon)[Ui.Icon.Name(slot?.Icon ?? Ui.IconName.MagnifyingGlass).Micro],
            Input
                .Value(view.Search)
                .Type(InputType.Text)
                // A space when there is none of its own: the clear button reads :placeholder-shown.
                .Placeholder(slot?.Placeholder ?? RaskStrings.Get(RaskString.SelectSearchPlaceholder, "Search..."))
                .Role("combobox")
                .Autocomplete("off")
                .Autofocus()
                .Ref(_searchInput)
                .Attributes(SearchInputMarks())
                // The list's keys are the field's while it is typed into: Enter picks instead of submitting a
                // form around the select, and the arrows walk the rows instead of the caret.
                .Data("rask-contain-keys", UiListboxLook.ListKeys)
                .Class(UiSelectLook.SearchInput)
                .Aria(aria)
                .OnInput(text => SearchedAsync(text, slot?.OnInput ?? default, opens: false))
                .OnKeyDown(e => OnListKeyAsync(e, view)),
            slot?.Clearable == false
                ? null
                : Div.Class(UiSelectLook.SearchClear)[
                    Button
                        .Type(ButtonType.Button)
                        .TabIndex(-1)
                        .Class(UiSelectLook.SearchClearButton)
                        .Aria("label", RaskStrings.Get(RaskString.SelectSearchClear, "Clear command input"))
                        .Data("ui-button", "")
                        .OnClick(() => SearchedAsync(string.Empty, slot?.OnInput ?? default, opens: false))[
                        Ui.Icon.Name(Ui.IconName.XMark).Micro
                    ]
                ]
        ];
    }
}
