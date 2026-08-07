namespace Rask.Example.Shared.Features;

// The column chooser and reordering. ColumnChooser adds a "Columns" menu above the grid: a checkbox to show
// or hide each column, and move earlier/later buttons to reorder it. Every action is a real button or
// checkbox, so the whole thing works from the keyboard alone — dragging a header onto another is only a mouse
// accelerator over the same handlers.
//
// Both axes are token lists of Field names, exactly like Grouped: `_hidden` is what is hidden ("region"),
// `_order` is the display order ("amount", "name", ...). Because they are just tokens, a real app persists
// them straight into the URL (?hide=region&cols=amount,name,region), so a laid-out grid survives a reload and
// a share — the same trick the grouping guide shows. Here they live in component state to keep the demo short.
//
// A grouped column already folds away on its own; hiding is the other reason a column leaves the table. The
// two compose: the grid funnels reorder, hide and grouped-away through one visible-column list, so sort,
// footers and the band colspans all follow without extra wiring.
public sealed partial class BsDataGridColumnsDemo : Component
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
    ];

    // Controlled visibility and order — the grid reports intent, this owns the state (a URL would, in an app).
    private List<string> _hidden = [];
    private List<string> _order = [];

    protected override Component? Render() =>
        Div(Id: "grid-columns-demo")[
            BsDataGrid(
                Id: "bs-grid-columns",
                Data: Deals,
                RowKey: d => d.Account,
                ColumnChooser: true,
                HiddenColumns: _hidden,
                OnHiddenColumnsChange: h => _hidden = [.. h],
                ColumnOrder: _order,
                OnColumnOrderChange: o => _order = [.. o],
                Columns:
                [
                    new BsColumn<Deal>
                    {
                        Title = "Account", Value = d => d.Account, Field = d => d.Account, Sortable = true,
                    },
                    new BsColumn<Deal>
                    {
                        Title = "Region", Value = d => d.Region, Field = d => d.Region, Sortable = true,
                    },
                    new BsColumn<Deal>
                    {
                        Title = "Rep", Value = d => d.Rep, Field = d => d.Rep, Sortable = true,
                    },
                    new BsColumn<Deal>
                    {
                        Title = "Amount", Class = Txt.End(), Field = d => d.Amount, Sortable = true,
                        SortKey = d => d.Amount,
                        Value = d => d.Amount.ToString("C0"),
                        Footer = rows => rows.Sum(d => d.Amount).ToString("C0"),
                    },
                ])];
}
