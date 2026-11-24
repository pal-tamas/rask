using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TableForMembersExample : Component
{
    private static readonly Tank[] Tanks =
    [
        new() { Name = "North yard", Size = 12000, Stock = 8450.5m, Fuel = Fuel.Diesel, Inspected = new DateOnly(2026, 3, 14), Shared = true },
        new() { Name = "Depot B", Size = 5000, Stock = 1200m, Fuel = Fuel.Petrol, Shared = false, Code = "T-7" },
    ];

    protected override Component? Render() =>
        Ui.Table.Id("ui-tanks")[
            Ui.TableColumns[
                // Each heading is the member's [Display(Name)]; a number keeps to the end; Code is [ScaffoldColumn(false)].
                Ui.TableColumn.For<Tank>(t => t.Name),
                Ui.TableColumn.For<Tank>(t => t.Fuel),
                Ui.TableColumn.For<Tank>(t => t.Size),
                Ui.TableColumn.For<Tank>(t => t.Stock),
                Ui.TableColumn.For<Tank>(t => t.Inspected),
                Ui.TableColumn.For<Tank>(t => t.Shared),
                Ui.TableColumn.For<Tank>(t => t.Code)
            ],
            Ui.TableRows[Tanks.Select(tank => Ui.TableRow.Key(tank.Name)[
                // Each cell is the value written for the reader: the format, the enum's label, "–" for none, Yes or No.
                Ui.TableCell.For(() => tank.Name).Variant(Ui.TableCellVariant.Strong),
                Ui.TableCell.For(() => tank.Fuel),
                Ui.TableCell.For(() => tank.Size),
                Ui.TableCell.For(() => tank.Stock),
                Ui.TableCell.For(() => tank.Inspected),
                Ui.TableCell.For(() => tank.Shared),
                Ui.TableCell.For(() => tank.Code)
            ])]
        ];

    public enum Fuel
    {
        Diesel,

        [Display(Name = "Petrol (95)")]
        Petrol,
    }

    // An ordinary row: the attributes say how a list names and writes each member.
    public sealed class Tank
    {
        [Display(Name = "Tank")]
        public string Name { get; init; } = "";

        [Display(Name = "Fuel")]
        public Fuel Fuel { get; init; }

        [Display(Name = "Size (L)")]
        public int Size { get; init; }

        [Display(Name = "In stock (L)")]
        [DisplayFormat(DataFormatString = "{0:N1}")]
        public decimal Stock { get; init; }

        [Display(Name = "Inspected")]
        [DisplayFormat(NullDisplayText = "not yet")]
        public DateOnly? Inspected { get; init; }

        [Display(Name = "Shared")]
        public bool Shared { get; init; }

        [ScaffoldColumn(false)]
        public string? Code { get; init; }
    }
}
