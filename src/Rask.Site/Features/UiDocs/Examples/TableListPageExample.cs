using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TableListPageExample(RouteState route) : Component
{
    private const int PerPage = 5;

    private static readonly Supplier[] Everyone =
    [
        new("Aminoff Freight", "Oslo"), new("Bothman & Sons", "Leeds"), new("Bushevi Fuel", "Tirana"),
        new("Geidt Logistics", "Porto"), new("George Haulage", "Cork"), new("Levin Oil", "Tartu"),
        new("Lipshutz Tank Co.", "Basel"), new("Lubin Lubricants", "Gdańsk"), new("Nolan Energy", "Galway"),
        new("Odum Petroleum", "Ghent"), new("Pereira Gas", "Braga"), new("Quist Depot", "Aarhus"),
    ];

    private List<Supplier> _suppliers = [.. Everyone];

    // A page reads its keys as [QueryParam("q")] properties; this example shares its page's address, so it reads them there.
    private string? Search => Read("q");

    protected override Component? Render()
    {
        var found = _suppliers.Where(supplier => Search is null || supplier.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();

        return Div.Class("grid gap-3")[
            Div.Class("flex flex-wrap items-center gap-2")[
                Ui.Input.Value(Search ?? "").Key("search").Class("w-56").Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search suppliers")
                    .OnChange((string text) => Find(text)),
                Ui.Spacer,
                Ui.Button.Key("delete-all").Sm.OnClick(() => { _suppliers.Clear(); })["Delete all"],
                Ui.Button.Key("restore").Sm.OnClick(() => { _suppliers = [.. Everyone]; })["Restore"]
            ],
            Ui.Table.Id("ui-suppliers").Class("w-full").ContainerClass("min-w-0")
                .SortIn("sort", "dir")
                .PageIn("page", found.Count, PerPage)
                .Empty("No suppliers yet.", "No supplier matches the search.")[
                Ui.TableColumns[
                    Ui.TableColumn.Key("name").Sortable()["Supplier"],
                    Ui.TableColumn.Key("city").Class("w-36").Sortable()["City"],
                    Ui.TableColumn.Key("delete").Class("w-16")
                ],
                Ui.TableRows[
                    Shown(found).Select(supplier => Ui.TableRow.Key(supplier.Name)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate")[supplier.Name],
                        Ui.TableCell[supplier.City],
                        Ui.TableCell.End.Class("py-0")[
                            // Flux's row action — a ghost, small, square button — with the outline drawing in the muted ink.
                            Ui.Button.Ghost.Sm.Icon(Ui.IconName.Trash).IconVariant(Ui.IconVariant.Outline)
                                .Class("text-zinc-400 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-white").AriaLabel($"Delete {supplier.Name}").OnClick(() => { _suppliers.Remove(supplier); })
                        ]
                    ])
                ]
            ]
        ];
    }

    // The page the address names, in the order it names: what a page's own read of its rows does.
    private IEnumerable<Supplier> Shown(List<Supplier> found)
    {
        var byCity = string.Equals(Read("sort"), "city", StringComparison.Ordinal);
        var sorted = byCity ? found.OrderBy(supplier => supplier.City, StringComparer.Ordinal) : found.OrderBy(supplier => supplier.Name, StringComparer.Ordinal);
        var ordered = string.Equals(Read("dir"), UiTable.Descending, StringComparison.Ordinal) ? sorted.Reverse() : sorted;
        var page = int.TryParse(Read("page"), NumberStyles.None, CultureInfo.InvariantCulture, out var asked) && asked > 1 ? asked : 1;

        return ordered.Skip((page - 1) * PerPage).Take(PerPage);
    }

    private string? Read(string key) => route.Query.TryGetValue(key, out var value) && value.Count > 0 ? value.ToString() : null;

    // A search is a move of the address, from the list's first page; the table's sort stays.
    private static void Find(string text) =>
        Go.With(new KeyValuePair<string, string?>("q", string.IsNullOrWhiteSpace(text) ? null : text.Trim()), new KeyValuePair<string, string?>("page", null));

    private sealed record Supplier(string Name, string City);
}
