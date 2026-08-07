namespace Rask.Example.Shared.Features;

// Grouping: rows banded by a column's value, nested, collapsible, with a subtotal per band — and a panel to
// drive it.
//
// Field is what names a column — Field = s => s.Region reads the member and calls this column "region". That
// token is what Grouped carries and what a URL would (?group=region,rep). Value could never supply it: it is
// a Func, and a compiled delegate has no member name to read.
//
// The source list below is deliberately NOT ordered by region. It doesn't need to be: a band is a run of
// consecutive rows, so the grid orders by the group keys first and only then by whatever column the user
// sorted. Click "Amount" and the rows re-sort INSIDE each band, never across them.
//
// GroupPanel adds the chips and the per-header group control. Drag a header into the panel, drag the chips to
// renest, drag one out to ungroup — and every one of those is also a real button, so the whole thing works
// from the keyboard alone. Tab to a header's group control and press Enter.
//
// A grouped column folds away by default: its value is the same for every row in its band and already names
// the band header, so the column would be a run of duplicates. "Show grouped column" flips ShowGroupedColumns
// to keep it — the value then appears in the band header AND repeated down every row.
public sealed partial class BsDataGridGroupDemo : Component
{
    private sealed record Deal(string Account, string Region, string Rep, decimal Amount);

    private static readonly List<Deal> Deals =
    [
        new("Northwind", "EMEA", "Ana", 12_400m),
        new("Contoso", "AMER", "Bo", 4_805m),
        new("Fabrikam", "EMEA", "Ana", 31_000m),
        new("Tailspin", "APAC", "Cy", 2_750m),
        new("Adventure Works", "AMER", "Bo", 9_200m),
        new("Wingtip", "EMEA", "Dee", 18_600m),
        new("Litware", "AMER", "Ana", 7_300m),
        new("Proseware", "APAC", "Cy", 15_050m),
        new("Fourth Coffee", "EMEA", "Dee", 6_120m),
    ];

    private List<string> _grouped = ["region"];
    private bool _showGrouped;

    protected override Component? Render() =>
        Div(Id: "grid-group-demo")[
            // A stand-in for the drag panel: the same Grouped state, driven by buttons.
            Div(Class: Bs.Join(Display.Flex(), "gap-2", Margin.Bottom(3)))[
                BsButton(Id: "group-region", Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm,
                    Active: Is("region"), OnClick: () => _grouped = ["region"])["By region"],
                BsButton(Id: "group-nested", Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm,
                    Active: Is("region", "rep"), OnClick: () => _grouped = ["region", "rep"])["Region ▸ rep"],
                BsButton(Id: "group-none", Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm,
                    Active: _grouped.Count == 0, OnClick: () => _grouped = [])["Ungrouped"],
                BsButton(Id: "group-show-col", Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm,
                    Active: _showGrouped, OnClick: () => _showGrouped = !_showGrouped)["Show grouped column"]
            ],
            BsDataGrid(
                Id: "bs-grid-group",
                Data: Deals,
                RowKey: d => d.Account,
                Grouped: _grouped,
                OnGroupedChange: g => _grouped = [.. g],
                GroupPanel: true,
                GroupCollapsible: true,
                GroupSubtotals: true,
                ShowGroupedColumns: _showGrouped,
                Columns:
                [
                    new BsColumn<Deal> { Title = "Account", Value = d => d.Account, Field = d => d.Account, Sortable = true },
                    new BsColumn<Deal>
                    {
                        Title = "Region", Value = d => d.Region, Field = d => d.Region, Groupable = true,
                        Sortable = true,
                    },
                    new BsColumn<Deal>
                    {
                        Title = "Rep", Value = d => d.Rep, Field = d => d.Rep, Groupable = true, Sortable = true,
                    },
                    // The same Footer delegate totals the whole set in <tfoot> AND each band's subtotal —
                    // GroupSubtotals reuses it over the band's rows rather than inventing a second hook.
                    new BsColumn<Deal>
                    {
                        Title = "Amount", Class = Txt.End(), Field = d => d.Amount, Sortable = true,
                        SortKey = d => d.Amount,
                        Value = d => d.Amount.ToString("C0"),
                        Footer = rows => rows.Sum(d => d.Amount).ToString("C0"),
                    },
                ])];

    private bool Is(params string[] fields) => _grouped.SequenceEqual(fields);
}
