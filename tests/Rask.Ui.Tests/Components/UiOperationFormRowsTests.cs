using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A form over an operation that takes rows, written with the kit's table, as a generated Form.For's model is.</summary>
[Route("/ui-operation-form/rows")]
public sealed partial class OperationFormRowsPage : Component
{
    /// <summary>The plates "stored": what the form reads, and what its submit writes.</summary>
    public static List<string> Stored { get; set; } = [];

    /// <summary>How many times the form has read what is stored.</summary>
    public static int Reads { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Main[
            OperationForms.Of<LineInput>(
                "7",
                static _ =>
                {
                    Reads++;
                    return Task.FromResult<LineInput?>(new LineInput { Trucks = [.. Stored.Select(static p => new TruckRow { Plate = p, Load = 10 })] });
                },
                static (input, _) =>
                {
                    Stored = [.. input.Trucks.Select(static t => t.Plate)];
                    return Task.CompletedTask;
                },
                static () => true)[f => [
                    Ui.Table[
                        Ui.TableColumns[Ui.TableColumn["Plate"], Ui.TableColumn["Load"], Ui.TableColumn[""]],
                        Ui.TableRows[f.Trucks.Select((row, i) => Ui.TableRow.Key(i)[
                            Ui.TableCell[Ui.Input.Bind(() => row.Plate).Id("plate-" + i).InputClass("plate"), Ui.Error.For(() => row.Plate)],
                            Ui.TableCell[Ui.Input.Bind(() => row.Load).Id("load-" + i), Ui.Error.For(() => row.Load)],
                            Ui.TableCell[Ui.Button.Ghost.Sm.Id("remove-" + i).OnClick(() => f.Trucks.Remove(row))["Remove"]]
                        ])]
                    ],
                    Ui.Button.Id("add-truck").OnClick(() => f.Trucks.Add(new()))["Add a truck"],
                    Ui.Button.Id("save").Type(Ui.ButtonType.Submit)["Save"],
                ]]
        ];

    /// <summary>The form's model: one list of rows, as the generated one holds a <c>Children</c> list.</summary>
    public sealed class LineInput
    {
        /// <summary>The rows.</summary>
        public List<TruckRow> Trucks { get; set; } = [];
    }

    /// <summary>One row.</summary>
    public sealed class TruckRow
    {
        /// <summary>The truck's plate.</summary>
        public string Plate { get; set; } = "";

        /// <summary>What it carries; empty for a new row.</summary>
        public decimal? Load { get; set; }
    }
}

/// <summary>The rows of a form over an operation are added and taken away with the kit's own buttons, each row bound.</summary>
[Collection(OperationFormRowsCollection.Name)]
public sealed class UiOperationFormRowsTests
{
    [Fact]
    public async Task A_row_is_added_empty_and_a_row_taken_away_with_a_button_in_the_form()
    {
        OperationFormRowsPage.Stored = ["AAA-111"];
        var page = Page.Visit("/ui-operation-form/rows");

        var read = page.FindAll("input.plate").Count;
        await page.On("#add-truck").Click();
        var added = page.FindAll("input.plate").Count;
        var empty = page.Find("#load-1").Attribute("value");
        await page.On("#remove-0").Click();

        Assert.Equal(1, read);
        Assert.Equal(2, added);
        Assert.True(string.IsNullOrEmpty(empty));
        Assert.Single(page.FindAll("input.plate"));
        Assert.True(string.IsNullOrEmpty(page.Find("#plate-0").Attribute("value")));
    }

    [Fact]
    public async Task A_saved_form_reads_what_it_saved_afresh()
    {
        OperationFormRowsPage.Stored = ["AAA-111"];
        var page = Page.Visit("/ui-operation-form/rows");
        var before = OperationFormRowsPage.Reads;

        await page.On("#add-truck").Click();
        await page.On("#plate-1").Change("BBB-222");
        await page.On("#load-1").Change("5");
        await page.On("form").Submit();

        Assert.Equal(["AAA-111", "BBB-222"], OperationFormRowsPage.Stored);
        Assert.Equal(before + 1, OperationFormRowsPage.Reads);
        Assert.Equal("BBB-222", page.Find("#plate-1").Attribute("value"));
    }
}

/// <summary>The rows page keeps what it stores in statics: its tests run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OperationFormRowsCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "operation-form-rows";
}
