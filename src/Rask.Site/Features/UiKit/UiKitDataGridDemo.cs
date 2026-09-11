namespace Rask.Site.Features.UiKit;

/// <summary>
///     The data grid, in the four shapes worth seeing side by side.
/// </summary>
/// <remarks>
///     Every one of these is the same component: what differs is which state the page took ownership of.
///     The first grid owns nothing and holds its own sort, page and selection; the last hands its sort
///     and page to fields on this class, which is the shape a server-paged screen has.
/// </remarks>
public sealed partial class UiKitDataGridDemo : Component
{
    private sealed record Release(int Id, string Name, string Channel, int Downloads, DateOnly Shipped);

    private static readonly Release[] Catalog =
    [
        new(1, "Rask.Core", "stable", 18420, new DateOnly(2026, 2, 11)),
        new(2, "Rask.Ui", "stable", 9310, new DateOnly(2026, 3, 4)),
        new(3, "Rask.Cli", "stable", 7745, new DateOnly(2026, 1, 27)),
        new(4, "Rask.Data", "preview", 2180, new DateOnly(2026, 5, 19)),
        new(5, "Rask.Jobs", "preview", 1640, new DateOnly(2026, 6, 2)),
        new(6, "Rask.WebPush", "preview", 880, new DateOnly(2026, 6, 30)),
        new(7, "Rask.Signaling", "alpha", 210, new DateOnly(2026, 8, 8)),
    ];

    private const int UrlPageSize = 3;

    private IReadOnlyList<int> _selected = [];
    private string? _sort = "downloads";
    private bool _descending = true;
    private int _page;

    /// <summary>The page of the last grid, counted from one, as the docs page read it from <c>?page=</c>.</summary>
    public int? UrlPage { get; set; }

    private int UrlPageIndex => Math.Clamp((UrlPage ?? 1) - 1, 0, (Catalog.Length - 1) / UrlPageSize);

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Section(
            "Columns are a factory, not a list",
            "The indexer takes a lambda whose parameter is the grid, and that is what fixes the row "
            + "type. Written as a flat child, a column would have nothing to infer its own lambda from.",
            Div.Data(Testid("ui-grid-basic"))[
                UiDataGrid.Data(Catalog).RowKey(r => r.Id).Zebra(true).Label("Packages")[c => [
                    c.Field(r => r.Name).Title("Package").Sortable(true),
                    c.Field(r => r.Channel).Title("Channel")
                        .Cell(r => UiBadge.Tone(r.Channel == "stable" ? UiTone.Success : UiTone.Info)[r.Channel]),
                    c.Field(r => r.Downloads).Title("Downloads").Sortable(true).Class("text-right")
                        .Footer(rows => rows.Sum(x => x.Downloads)),
                ]]
            ]),

        Section(
            "Selection is typed",
            "RowKey says what identifies a row. It is required, so every grid has one, and the type it "
            + "pins is what the selection is expressed in: the keys arriving back are an "
            + "IReadOnlyList<int> rather than boxed objects, inferred from the lambda.",
            Div.Data(Testid("ui-grid-selection"))[
                UiDataGrid.Data(Catalog)
                    .RowKey(r => r.Id)
                    .Label("Packages to publish")
                    .Selected(_selected)
                    .OnSelectionChange(keys => _selected = keys)[c => [
                        c.Field(r => r.Name).Title("Package"),
                        c.Field(r => r.Shipped).Title("Shipped").Class("text-right"),
                    ]],
                P.Class("mt-2 text-sm text-ui-muted").Data(Testid("ui-grid-selection-state"))[
                    _selected.Count == 0
                        ? "Nothing selected."
                        : $"Selected ids: {string.Join(", ", _selected.Order())}."
                ]
            ]),

        Section(
            "Grouping, subtotals and the column chooser",
            "Group from a column's header button, then reorder or remove the grouping in the panel. "
            + "Both the panel and the chooser move things with buttons — drag is added on top of them, "
            + "because HTML5 drag fires on neither touch nor a keyboard.",
            Div.Data(Testid("ui-grid-grouped"))[
                UiDataGrid.Data(Catalog)
                    .RowKey(r => r.Id)
                    .Label("Packages by channel")
                    .GroupPanel(true)
                    .ColumnChooser(true)
                    .GroupSubtotals(true)
                    .Detail(r => P.Class("text-sm text-ui-muted")[
                        $"{r.Name} shipped on {r.Shipped:d MMMM yyyy}."
                    ])[c => [
                        c.Field(r => r.Channel).Title("Channel").Groupable(true),
                        c.Field(r => r.Name).Title("Package").Sortable(true),
                        c.Field(r => r.Downloads).Title("Downloads").Class("text-right")
                            .Footer(rows => rows.Sum(x => x.Downloads)),
                    ]]
            ]),

        Section(
            "Handing the state over",
            "Name a state and its callback and that axis belongs to the page. Here the sort and the "
            + "page do; the expander and the column layout carry on holding their own.",
            Div.Data(Testid("ui-grid-controlled"))[
                UiDataGrid.Data(Catalog)
                    .RowKey(r => r.Id)
                    .Label("Packages, paged by the page")
                    .PageSize(3)
                    .Page(_page)
                    .OnPageChange(page => _page = page)
                    .Sort(_sort)
                    .SortDescending(_descending)
                    .OnSortChange(sort => { _sort = sort.Field; _descending = sort.Descending; })[c => [
                        c.Field(r => r.Name).Title("Package").Sortable(true),
                        c.Field(r => r.Downloads).Title("Downloads").Sortable(true).Class("text-right"),
                    ]],
                P.Class("mt-2 text-sm text-ui-muted").Data(Testid("ui-grid-controlled-state"))[
                    $"Page {_page + 1}, sorted by {_sort ?? "nothing"} "
                    + (_descending ? "descending." : "ascending.")
                ]
            ]),

        Section(
            "An operator's table",
            "Secondary columns wait until the table has room for them, while the phone's stacked lines "
            + "still list every one. A row can carry a tone, ids are set in mono, and the toolbar is one row "
            + "that stacks on a phone. The pages are links: this page's ?page= is the grid's page, so a page "
            + "can be shared and the back button walks back through them.",
            Div.Data(Testid("ui-grid-console"))[
                UiDataGrid.Data(Catalog.Skip(UrlPageIndex * UrlPageSize).Take(UrlPageSize))
                    .RowKey(r => r.Id)
                    .Label("Packages, paged by the URL")
                    .PageSize(UrlPageSize)
                    .Page(UrlPageIndex)
                    .TotalCount(Catalog.Length)
                    .PageHref(page => Routes.UiKitDataGridPage(Page: page + 1))
                    .RowTone(r => r.Downloads > 9000 ? UiTone.Success : null)
                    .Toolbar(UiBadge.Tone(UiTone.Info)[$"{Catalog.Length} packages"])[c => [
                        c.Field(r => r.Id).Title("#").Mono(true),
                        c.Field(r => r.Name).Title("Package"),
                        c.Field(r => r.Channel).Title("Channel").ShowFrom(UiBreakpoint.Md),
                        c.Field(r => r.Shipped).Title("Shipped").ShowFrom(UiBreakpoint.Lg),
                    ]]
            ])
    ];

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
