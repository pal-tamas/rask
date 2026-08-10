namespace Rask.Example.Shared.Features;

// A tall BsDataGrid<T> that scrolls in its own box with a frozen header: MaxHeight bounds the scroll
// container and StickyHeader pins the header row to it.
//
// The two go together. A sticky header sticks to its nearest scroll container, so without MaxHeight there is
// no bounded container to stick to and the header just scrolls away with the page.
//
// No PageSize here on purpose: scrolling is the alternative to paging for a set this size. Sorting still works
// — click a header and the rows reorder under it.
public sealed partial class BsDataGridStickyDemo : Component
{
    private sealed record Reading(string Sensor, string Zone, double Celsius);

    private static readonly List<Reading> Readings = Build();

    private static List<Reading> Build()
    {
        string[] zones = ["North", "South", "East", "West"];
        var rows = new List<Reading>();
        for (var i = 1; i <= 40; i++)
        {
            rows.Add(new Reading($"SENSOR-{i:D3}", zones[i % zones.Length], 18.0 + (i % 13) * 0.7));
        }

        return rows;
    }

    protected override Component? Render() =>
        Div.Id("grid-sticky-demo")[
            BsDataGrid(
                Id: "bs-grid-sticky",
                Data: Readings,
                RowKey: r => r.Sensor,
                MaxHeight: "280px",
                StickyHeader: true,
                Columns:
                [
                    new BsColumn<Reading> { Title = "Sensor", Value = r => r.Sensor, Sortable = true },
                    new BsColumn<Reading> { Title = "Zone", Value = r => r.Zone, Sortable = true },
                    new BsColumn<Reading>
                    {
                        Title = "Temp", Class = Txt.End(), Sortable = true, SortKey = r => r.Celsius,
                        Value = r => $"{r.Celsius:F1} °C",
                    },
                ])];
}
