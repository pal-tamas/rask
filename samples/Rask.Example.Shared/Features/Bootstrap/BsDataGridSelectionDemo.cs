namespace Rask.Example.Shared.Features;

// Row selection driving a bulk action. Selectable adds the leading checkbox column; the grid tracks the
// selection itself and reports the full set of keys through OnSelectionChange, which is what the toolbar
// above the grid renders from.
//
// RowKey is what makes this work: the selection is tracked by KEY, not by position, so it follows the rows
// through a sort and accumulates across pages. Pick a row here, sort, page — it stays picked.
//
// The reported keys are RowKey values, not rows. The grid only ever holds the current page (under TotalCount
// or an IQueryable it has never seen the rest), so mapping keys back to entities is the caller's job — and a
// real app re-checks them server-side before acting, since a key can name a row that has since been deleted.
public sealed partial class BsDataGridSelectionDemo : Component
{
    private sealed record Task_(int Id, string Title, string Assignee, string State);

    private static readonly List<Task_> Seed =
    [
        new(1, "Ship the release notes", "Ana", "Todo"),
        new(2, "Fix the flaky sort test", "Bo", "Doing"),
        new(3, "Upgrade the SDK", "Ana", "Todo"),
        new(4, "Write the migration guide", "Cy", "Done"),
        new(5, "Trim the WASM bundle", "Bo", "Doing"),
        new(6, "Audit the aria labels", "Cy", "Todo"),
    ];

    private List<Task_> _tasks = [.. Seed];
    private IReadOnlyList<object> _selected = [];
    private string? _done;

    protected override Component? Render() =>
        Div.Id("grid-selection-demo")[
            Div.Class(Bs.Join(Display.Flex(), Flex.Align(BsAlign.Center), "gap-2", Margin.Bottom(3)))[
                BsButton
                    .Id("grid-bulk-archive")
                    .Color(BsColor.Danger)
                    .Disabled(_selected.Count == 0)
                    .OnClick(Archive)[$"Archive {_selected.Count} selected"],
                _done is not null ? Span.Id("grid-bulk-done").Class(Txt.Color(BsColor.Secondary))[_done] : null
            ],
            BsDataGrid(
                Id: "bs-grid-selection",
                Data: _tasks,
                Selectable: true,
                RowKey: t => t.Id,
                // The full set of selected keys after every click — not a delta.
                OnSelectionChange: keys => _selected = keys,
                Empty: BsAlert.Id("grid-selection-empty").Color(BsColor.Success)["Nothing left. Archived it all."],
                Columns:
                [
                    new BsColumn<Task_> { Title = "Task", Value = t => t.Title, Sortable = true },
                    new BsColumn<Task_> { Title = "Assignee", Value = t => t.Assignee, Sortable = true },
                    new BsColumn<Task_>
                    {
                        Title = "State", Sortable = true, SortKey = t => t.State, Template = StateBadge,
                    },
                ])];

    private void Archive()
    {
        // The keys are RowKey values — here the int Id. A real app would re-check them against the store.
        var ids = _selected.Cast<int>().ToHashSet();
        _tasks = _tasks.Where(t => !ids.Contains(t.Id)).ToList();
        _done = $"Archived {ids.Count}.";
        _selected = [];
    }

    private static Component StateBadge(Task_ task) =>
        BsBadge
            .Color(task.State switch
            {
                "Done" => BsColor.Success,
                "Doing" => BsColor.Warning,
                _ => BsColor.Secondary,
            })[task.State];
}
