using Rask.Core;

#pragma warning disable RASK014 // the tests own the root they re-render; there is no parent to build it
#pragma warning disable RASK019 // a test page; its <head> is not what is under test

namespace Rask.UiTests.Live;

/// <summary>
///     Eszaf's report form, its columns tab as the app writes it: the columns shown, the grouped ones first, each a kit row
///     keyed by its column, moved up and down among those of its kind by buttons that are disabled at its ends.
/// </summary>
internal sealed partial class ReportColumnsDraft : Component
{
    internal static readonly string[] Catalogue = ["Km", "Dátum", "Mennyiség", "EO", "Jármű", "Termék"];

    internal List<Column> Columns { get; } = [];

    internal string[] Names => [.. Columns.Select(static column => column.Name)];

    protected override Component? Render() =>
        Div.Class("space-y-6")[
            Ui.Button.Key("add").Icon(Ui.IconName.Plus).OnClick(Add)["Oszlopok hozzáadása"],
            Columns.Count == 0 ? Ui.Text.Data("empty-list", "")["Még nincs oszlop hozzáadva."] : Table()
        ];

    private Component Table()
    {
        var summable = Columns.Where(static c => c.Summable).Select(static c => c.Id).ToList();

        return Ui.Table.Class("w-full min-w-[48rem]")[
            Ui.TableColumns[
                Ui.TableColumn.Key("column")["Oszlop"],
                Ui.TableColumn.Key("group").Align(Ui.Align.Center)["Csoport"],
                Ui.TableColumn.Key("sum").Align(Ui.Align.Center)["Összeg"],
                Ui.TableColumn.Key("avg").Align(Ui.Align.Center)["Átlag"],
                Ui.TableColumn.Key("sums")["Összegek"],
                Ui.TableColumn.Key("avgs")["Átlagok"],
                Ui.TableColumn.Key("actions")["Műveletek"]
            ],
            Ui.TableRows[
                Columns.Select(column => Ui.TableRow.Key("column-" + column.Id)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[column.Name],
                    Ui.TableCell.Align(Ui.Align.Center)[column.Groupable ? Tick(column.Group, _ => ToggleGroup(column), "Csoport: " + column.Name) : null],
                    Ui.TableCell.Align(Ui.Align.Center)[column.Summable ? Tick(column.Sum, on => column.Sum = on, "Összeg: " + column.Name) : null],
                    Ui.TableCell.Align(Ui.Align.Center)[column.Avgable ? Tick(column.Avg, on => column.Avg = on, "Átlag: " + column.Name) : null],
                    Ui.TableCell.Class("min-w-56")[column.Group ? Picker(() => column.Sums, summable, "Sums") : null],
                    Ui.TableCell.Class("min-w-56")[column.Group ? Picker(() => column.Avgs, summable, "Avgs") : null],
                    Ui.TableCell.End[
                        Div.Class("flex justify-end gap-1")[
                            Ui.Button.Key("up").Ghost.Sm.Square(true).Icon(Ui.IconName.ArrowUp).Tooltip("Feljebb").Aria("label", "Feljebb: " + column.Name)
                                .Disabled(IsFirst(column)).OnClick(() => Swap(column, -1)),
                            Ui.Button.Key("down").Ghost.Sm.Square(true).Icon(Ui.IconName.ArrowDown).Tooltip("Lejjebb").Aria("label", "Lejjebb: " + column.Name)
                                .Disabled(IsLast(column)).OnClick(() => Swap(column, 1)),
                            Ui.Button.Key("remove").Ghost.Sm.Square(true).Icon(Ui.IconName.Trash).Tooltip("Törlés").Aria("label", "Törlés: " + column.Name)
                                .OnClick(() => Columns.RemoveAll(other => other.Id == column.Id))
                        ]
                    ]
                ]).ToList()
            ]
        ];
    }

    private static Component Tick(bool on, Action<bool> tick, string name) =>
        Ui.Checkbox.Checked(on).OnChange(tick).Attributes(new Dictionary<string, string?> { ["aria-label"] = name });

    private static Component Picker(System.Linq.Expressions.Expression<Func<List<int>>> field, List<int> choices, string scope) =>
        Ui.Pillbox.Bind(field).Searchable(true).Placeholder("Válasszon…")[
            choices.Select(id => Ui.PillboxOption.Key(scope + ":" + id).Value(id)["Oszlop " + id])];

    private void Add()
    {
        Columns.AddRange(Catalogue.Select((name, id) => new Column(id + 1, name)));
        Arrange();
    }

    private void ToggleGroup(Column column)
    {
        column.Group = !column.Group;
        Arrange();
    }

    private bool IsFirst(Column column) => Columns.FirstOrDefault(other => other.Group == column.Group) == column;

    private bool IsLast(Column column) => Columns.LastOrDefault(other => other.Group == column.Group) == column;

    private void Swap(Column column, int by)
    {
        var kind = Columns.Where(other => other.Group == column.Group).ToList();
        var at = kind.IndexOf(column);
        if (at + by < 0 || at + by >= kind.Count)
        {
            return;
        }

        var neighbour = kind[at + by];
        (Columns[Columns.IndexOf(column)], Columns[Columns.IndexOf(neighbour)]) = (neighbour, column);
    }

    private void Arrange()
    {
        var arranged = Columns.Where(static c => c.Group).Concat(Columns.Where(static c => !c.Group)).ToList();
        Columns.Clear();
        Columns.AddRange(arranged);
    }

    internal sealed class Column(int id, string name)
    {
        internal int Id { get; } = id;

        internal string Name { get; } = name;

        internal bool Groupable => Name is "Jármű" or "Termék";

        internal bool Summable => Name is "Mennyiség" or "EO";

        internal bool Avgable => false;

        internal bool Group { get; set; }

        internal bool Sum { get; set; }

        internal bool Avg { get; set; }

        internal List<int> Sums { get; set; } = [];

        internal List<int> Avgs { get; set; } = [];
    }
}
