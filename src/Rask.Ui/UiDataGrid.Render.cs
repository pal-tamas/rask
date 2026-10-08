using System.Globalization;
using System.Linq.Expressions;

namespace Rask;

public sealed partial class UiDataGrid<T, TKey>
{
    // The automatic card layout is the SAME cells under different utilities, so it costs nothing but the
    // classes. It is off wherever the caller drew their own card, because then there are two layouts and
    // the table is simply hidden below sm.
    //
    // EVERY responsive class here is a `max-sm:` VARIANT, never a base utility plus an `sm:` override,
    // and that is a cross-stylesheet rule rather than a preference. The kit ships its own compiled sheet
    // and the consuming app links its own after it; CSS layers do not merge across <link> sheets, so an
    // app that writes `hidden` anywhere emits an unconditional `.hidden{display:none}` that lands LATER
    // in the cascade than this sheet's `sm:table-header-group` and wins at every width. Written the
    // other way round there is no base class to overrule: above `sm` the element keeps the display a
    // table gives it, and only the variant — a name the app's sheet has no reason to hold a different
    // opinion about — applies below.
    private bool StackedCards => Card is null;

    private string CellClass(UiColumn<T> column) =>
        UiClass.Compose(
            // The kit's marker for a body cell, which ui.css gives overflow-wrap:anywhere: a cell holding one
            // unbroken token — a type name, a request id, a path — otherwise sets the table's minimum width, and
            // the table spills out of a phone. A rule rather than the wrap-anywhere utility because the property
            // INHERITS: a badge or a button in the cell then broke its own label letter by letter inside its
            // fixed height — a level badge reading "mati" for "Information" — and only a rule can put those back.
            "ui-grid-cell",
            StackedCards
                ? "max-sm:flex max-sm:items-baseline max-sm:justify-between max-sm:gap-3 "
                  + "max-sm:before:font-medium max-sm:before:text-base-content/60 "
                  + "max-sm:before:content-[attr(data-label)]"
                : "",
            column.CellClasses);

    private IReadOnlyDictionary<string, string?>? TableAria()
    {
        if (Label is not { } label)
        {
            return Busy ? UiDataGridAria.Busy : null;
        }

        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = label };
        if (Busy)
        {
            aria["busy"] = "true";
        }

        return aria;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // ONCE per render — see ResolveColumns. Everything below reads this list.
        var columns = ResolveColumns();
        var visible = VisibleColumns(columns);
        var groups = GroupColumns(columns);
        var rows = ResolveRows(columns);
        var span = visible.Count + LeadingCells;

        var table = Table
            .Class(UiClass.Compose(
                "table w-full",
                Zebra is true ? "table-zebra" : "",
                Size is { } size ? UiClassNames.TableSize(size) : "",
                StickyHeader is true ? "table-pin-rows" : ""))
            // One Aria call, not two: a second would replace the first outright rather than add to it,
            // which is what RASK044 reports. aria-busy goes on the TABLE rather than on a wrapper around
            // the spinner, because a live region inside an aria-busy subtree has its announcement
            // deferred until busy clears — by which point the load is over and was never announced.
            .Aria(TableAria())[
            Head(visible, rows.Rows),
            Tbody[Body(visible, groups, rows, span)],
            Foot(visible, rows.All)
        ];

        var scroller = Div
            .Class(UiClass.Compose(
                "overflow-x-auto rounded-xl border border-base-300 bg-base-100",
                MaxHeight is not null ? "overflow-y-auto" : "",
                Busy ? "opacity-60" : "",
                Card is not null ? "max-sm:hidden" : ""))
            .Style(MaxHeight is { } max ? "max-height:" + max : null)[table];

        return Div.Class(UiClass.Compose("flex flex-col gap-3", Class))[
            ToolbarRow(),
            Chrome(columns, groups),
            scroller,
            Cards(rows.Rows),
            Pager(rows)
        ];
    }

    private Component Head(IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<T> pageRows) =>
        Thead.Class(StackedCards ? "max-sm:hidden" : null)[
            Tr[
                SelectionEnabled ? Th.Class("w-0")[SelectAllBox(pageRows)] : null,
                Expandable ? Th.Class("w-0").AriaLabel("Expand") : null,
                visible.Select(HeaderCell)
            ]
        ];

    private IReadOnlyDictionary<string, string?> SortAria(bool sorted) =>
        (sorted, CurrentSortDescending) switch
        {
            (false, _) => UiDataGridAria.SortNone,
            (_, true) => UiDataGridAria.SortDescending,
            _ => UiDataGridAria.SortAscending,
        };

    private Ui.IconName SortIcon(bool sorted) =>
        (sorted, CurrentSortDescending) switch
        {
            (false, _) => Ui.IconName.ArrowsUpDown,
            (_, true) => Ui.IconName.ChevronDown,
            _ => Ui.IconName.ChevronUp,
        };

    private Component HeaderCell(UiColumn<T> column)
    {
        var sorted = column.FieldName is { } token
            && string.Equals(CurrentSort, token, StringComparison.Ordinal);

        // A column can only be sorted by name, so one with no field token gets a plain header rather
        // than a control that would do nothing.
        var sortable = column.Sortable is true && column.FieldName is not null;
        var groupable = column.Groupable is true && column.FieldName is not null;

        var head = Th
            .Key(column.FieldName ?? column.Title ?? "")
            .Scope(ThScope.Col)
            .Class(column.HeaderClasses);

        // Only where there is a sort state to report. Passing null writes a BARE `aria-sort`, which is
        // not "no sort state" — it is an aria-sort with no value, on a header that cannot be sorted.
        if (sortable)
        {
            head = head.Aria(SortAria(sorted));
        }

        return head[
            Div.Class("flex items-center gap-1")[
                sortable
                    ? Button
                        .Type(ButtonType.Button)
                        .Class("inline-flex items-center gap-1 font-medium hover:underline")
                        .Disabled(Busy)
                        .OnClick(() => ToggleSortAsync(column))[
                        column.Title ?? "",
                        Ui.Icon
                            .Name(SortIcon(sorted))
                            .Class("size-3 shrink-0 opacity-60")
                    ]
                    : Span[column.Title ?? ""],
                groupable ? GroupToggle(column) : null
            ]
        ];
    }

    private UiButton GroupToggle(UiColumn<T> column)
    {
        var token = column.FieldName!;
        var on = CurrentGrouped.Contains(token, StringComparer.Ordinal);
        return Ui.Button.Xs.Icon(Ui.IconName.RectangleStack)
            .AriaLabel(on ? "Stop grouping by " + (column.Title ?? token) : "Group by " + (column.Title ?? token))
            .AriaPressed(on ? AriaPressed.True : AriaPressed.False)
            .Variant(on ? Ui.ButtonVariant.Filled : Ui.ButtonVariant.Ghost)
            .Disabled(Busy)
            .OnClick(() => on ? UngroupAsync(token) : GroupByAsync(token));
    }

    private HTMLInputElement<bool> SelectAllBox(IReadOnlyList<T> pageRows)
    {
        // "Select all" would be a lie wherever a pager is: the grid holds one page and can only name the
        // keys it has.
        //
        // Of<bool>() rather than a value: the type argument is what makes the input a checkbox and
        // OnChange a bool.
        return Input
            .Of<bool>()
            .Checked(AllSelected(pageRows))
            .OnChange(on => SetPageSelectionAsync(pageRows, on))
            .Class("checkbox checkbox-sm")
            .AriaLabel("Select all rows on this page")
            .Disabled(Busy);
    }

    private IEnumerable<Component?> Body(
        IReadOnlyList<UiColumn<T>> visible, List<UiColumn<T>> groups, Resolved rows, int span)
    {
        if (rows.Rows.Count == 0)
        {
            yield return Tr[
                Td.ColSpan(span).Class("py-10 text-center text-base-content/60")[
                    Empty ?? (Component)"Nothing to show."
                ]
            ];
            yield break;
        }

        if (groups.Count == 0)
        {
            foreach (var component in Rows(visible, rows.Rows, span))
            {
                yield return component;
            }

            yield break;
        }

        foreach (var component in Bands(visible, groups, rows.Rows, span))
        {
            yield return component;
        }
    }

    private IEnumerable<Component?> Rows(IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<T> rows, int span)
    {
        // A cell's class depends on its column and on the grid, never on the row, so it is composed once per
        // column here rather than once per cell. A polling grid re-renders on every update, and composing per cell
        // was a builder and a string for every cell of every row, every time, for the same few values.
        var classes = new string[visible.Count];
        var clickable = OnRowClick.HasValue ? new string[visible.Count] : null;
        for (var c = 0; c < visible.Count; c++)
        {
            classes[c] = CellClass(visible[c]);
            if (clickable is not null)
            {
                clickable[c] = UiClass.Compose(classes[c], "cursor-pointer");
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = RowIdentity(row);
            var open = Expandable && _expanded.Contains(key);

            yield return Tr
                .Key(key)
                .Class(UiClass.Compose(
                    StackedCards ? "max-sm:block max-sm:border-b max-sm:border-base-300" : "",
                    Hover ?? OnRowClick.HasValue
                        ? "hover:bg-base-200"
                        : "",
                    RowTone?.Invoke(row) is { } tone ? UiClassNames.RowTone(tone) : "",
                    RowClass?.Invoke(row)))[
                SelectionEnabled ? Td.Class("w-0")[SelectBox(row)] : null,
                Expandable ? Td.Class("w-0")[Expander(row, key, open)] : null,
                Cells(visible, row, classes, clickable)
            ];

            if (open && Detail?.Invoke(row) is { } detail)
            {
                yield return Tr.Key(key.ToString() + "-detail")[
                    Td.ColSpan(span).Class("bg-base-200/50")[detail]
                ];
            }
        }
    }

    private Component?[] Cells(IReadOnlyList<UiColumn<T>> visible, T row, string[] classes, string[]? clickable)
    {
        var cells = new Component?[visible.Count];
        for (var c = 0; c < visible.Count; c++)
        {
            cells[c] = Cell(visible[c], row, classes[c], clickable?[c]);
        }

        return cells;
    }

    private Component Cell(UiColumn<T> column, T row, string cellClass, string? clickableClass)
    {
        var cell = Td
            .Key(column.FieldName ?? column.Title ?? "")
            .Class(cellClass);

        // Only where the stacked layout will read it. Passing null writes a BARE `data-label`, so a grid
        // with its own card markup carried an empty attribute on every cell it had.
        if (StackedCards && column.Title is { } title)
        {
            cell = cell.Data("label", title);
        }

        // The row-click handler goes on the CELLS rather than the row, so a column can carve itself out
        // of it — see Ui.Column.RowClickable for why a custom cell does so by default.
        if (column.IsRowClickable && clickableClass is not null && RowClickHandler(row) is { } click)
        {
            cell = cell.OnClick(click).Class(clickableClass);
        }

        return cell[column.Body(row)];
    }

    // One handler either way: `Invoke` returns a completed ValueTask for a synchronous one, and the cell's
    // OnClick takes the ValueTask as it is.
    private Func<ValueTask>? RowClickHandler(T row) =>
        OnRowClick.HasValue ? () => OnRowClick.Invoke(row) : null;

    private HTMLInputElement<bool> SelectBox(T row)
    {
        return Input
            .Of<bool>()
            .Checked(IsSelected(row))
            .OnChange(on => ToggleAsync(row, on))
            .Class("checkbox checkbox-sm")
            .AriaLabel("Select row")
            .Disabled(Busy);
    }

    private Component Expander(T row, object key, bool open)
    {
        if (Detail?.Invoke(row) is null)
        {
            return Span;
        }

        return Ui.Button.Ghost.Xs.Icon(open ? Ui.IconName.ChevronDown : Ui.IconName.ChevronRight)
            .AriaLabel(open ? "Collapse row" : "Expand row")
            .OnClick(() => ToggleExpand(key));
    }

    private Component? Foot(IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<T> all)
    {
        if (!visible.Any(static c => c.HasFooter))
        {
            return null;
        }

        return Tfoot.Class(StackedCards ? "max-sm:hidden" : null)[
            Tr[
                LeadingCells > 0 ? Td.ColSpan(LeadingCells) : null,
                visible.Select(column =>
                    Td.Key(column.FieldName ?? column.Title ?? "").Class(column.CellClasses)[column.Foot(all)])
            ]
        ];
    }

    private Component? Cards(IReadOnlyList<T> rows) =>
        Card is not { } card
            ? null
            : Div.Class("max-sm:flex max-sm:flex-col max-sm:gap-2 sm:hidden")[
                rows.Select((row, i) =>
                    Div.Key(RowIdentity(row))
                        .Class("rounded-xl border border-base-300 bg-base-100 p-3")[card.Invoke(row)])
            ];

    // One row of controls from sm up, one control per line below it: three filters side by side at 360px
    // leave each too narrow to show the value it is set to, which is the one thing a filter has to show.
    // Stacked through max-sm: variants, for the same cross-sheet reason as the cells above.
    private Component? ToolbarRow() =>
        Toolbar is null
            ? null
            : Div.Class("flex flex-wrap items-center gap-2 max-sm:flex-col max-sm:items-stretch")[Toolbar];

    private Component? Pager(Resolved rows)
    {
        if (Paging <= 0 || rows.Pages <= 1)
        {
            return null;
        }

        var current = Math.Clamp(CurrentPage, 0, rows.Pages - 1) + 1;

        // The pager counts from one and the grid from zero; the conversion happens here, once, in both modes.
        return Div.Class("flex flex-wrap items-center justify-between gap-2")[
            Span.Class("text-sm text-base-content/60")[
                rows.Total.ToString(CultureInfo.InvariantCulture) + " rows"
            ],
            PageHref is { } href
                ? Ui.Pagination
                    .Pages(rows.Pages)
                    .Current(current)
                    .Href(page => href.Invoke(page - 1))
                : Ui.Pagination
                    .Pages(rows.Pages)
                    .Current(current)
                    .OnPage(page => _ = GoToPageAsync(page - 1, rows.Pages))
        ];
    }
}
