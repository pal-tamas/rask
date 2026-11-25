using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     A form over an operation that takes rows, each row picking from the same options in a select of its own: the
///     options of every row are keyed alike, by the id they offer.
/// </summary>
[Route("/ui-operation-form/fuel-rows")]
public sealed partial class OperationFormFuelRowsPage : Component
{
    /// <summary>The fuels every row offers, keyed by the same ids in each.</summary>
    public static readonly (string Id, string Name)[] Fuels = [("diesel", "Diesel"), ("petrol", "Petrol"), ("gas", "Gas")];

    /// <summary>What the last save wrote, a fuel per row.</summary>
    public static IReadOnlyList<string?> Saved { get; set; } = [];

    /// <inheritdoc />
    protected override Component? Render() =>
        Main[
            OperationForms.Of<TankInput>(
                "tanks",
                static _ => Task.FromResult<TankInput?>(new TankInput { Tanks = [new TankRow(), new TankRow()] }),
                static (input, _) =>
                {
                    Saved = [.. input.Tanks.Select(static tank => tank.Fuel)];
                    return Task.CompletedTask;
                },
                static () => true)[f => [
                    Ui.Table[
                        Ui.TableColumns[Ui.TableColumn["Fuel"]],
                        Ui.TableRows[f.Tanks.Select((row, i) => Ui.TableRow.Key(i)[
                            Ui.TableCell[Div.Id("fuel-" + i)[
                                Ui.Select.Bind(() => row.Fuel).Listbox.Placeholder("Choose…")[
                                    Fuels.Select(fuel => Ui.SelectOption.Key(fuel.Id).Value(fuel.Id)[fuel.Name])
                                ]
                            ]]
                        ])]
                    ],
                    Ui.Button.Id("save").Type(Ui.ButtonType.Submit)["Save"],
                ]]
        ];

    /// <summary>The form's model: the rows.</summary>
    public sealed class TankInput
    {
        /// <summary>The rows.</summary>
        public List<TankRow> Tanks { get; set; } = [];
    }

    /// <summary>One row: the fuel its tank holds.</summary>
    public sealed class TankRow
    {
        /// <summary>The fuel's id; none until one is picked.</summary>
        public string? Fuel { get; set; }
    }
}

/// <summary>Each row's select of a form over rows keeps its own pick, however the rows' picks follow one another.</summary>
public sealed class OperationFormRowPicksTests
{
    private static string[] Shown(Page page) => [page.TextOf("#fuel-0 [data-ui-select-button]"), page.TextOf("#fuel-1 [data-ui-select-button]")];

    [Fact]
    public async Task Picks_that_cross_between_two_rows_offering_the_same_ids_leave_each_row_its_own()
    {
        var page = Page.Visit("/ui-operation-form/fuel-rows");

        var said = new List<string[]>();
        foreach (var (row, fuel) in new[] { (0, "Diesel"), (1, "Petrol"), (0, "Petrol"), (0, "Diesel") })
        {
            await HeldList.PickIn(page, "#fuel-" + row, fuel);
            said.Add(Shown(page));
        }

        await page.On("form").Submit();

        Assert.Equal(
            [["Diesel", "Choose…"], ["Diesel", "Petrol"], ["Petrol", "Petrol"], ["Diesel", "Petrol"]],
            said);
        Assert.Equal(["diesel", "petrol"], OperationFormFuelRowsPage.Saved);
    }
}
