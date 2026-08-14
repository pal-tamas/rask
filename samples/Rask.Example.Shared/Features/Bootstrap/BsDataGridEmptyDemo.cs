namespace Rask.Example.Shared.Features;

// Empty replaces the whole grid — headers, pager and all — when there are no rows, so a filtered-to-nothing
// list reads as a deliberate message rather than an empty table. Filter the list away and back: the grid keeps
// the sort you chose, because sorting is the grid's own state and it survives the round-trip.
public sealed partial class BsDataGridEmptyDemo : Component
{
    private sealed record Task(string Title, string Owner);

    private static readonly List<Task> All =
    [
        new("Ship the data grid", "pt"),
        new("Write the guide", "pt"),
        new("Review the PR", "mt"),
    ];

    private string _filter = "";

    protected override Component? Render()
    {
        var rows = All.Where(t => t.Owner.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();

        return Div.Class(Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(3)))[
            Div.Class(Bs.Join(Display.Flex(), Flex.Gap(2)))[
                BsButton
                    .Id("grid-filter-none")
                    .Color(BsColor.Secondary)
                    .Outline(true)
                    .Size(BsSize.Sm)
                    .OnClick(() => _filter = "nobody")["Filter to nothing"],
                BsButton
                    .Id("grid-filter-clear")
                    .Color(BsColor.Secondary)
                    .Outline(true)
                    .Size(BsSize.Sm)
                    .OnClick(() => _filter = "")["Clear filter"]
            ],
            BsDataGrid
                .Data(rows)
                .Columns([
                    new BsColumn<Task> { Title = "Task", Value = t => t.Title, Sortable = true },
                    new BsColumn<Task> { Title = "Owner", Value = t => t.Owner, Sortable = true },
                ])
                .Id("bs-grid-empty")
                .RowKey(t => t.Title)
                .Empty(BsAlert.Id("grid-empty").Color(BsColor.Info).Class(Margin.Bottom(0))[
                    "No tasks match that filter."])
        ];
    }
}
