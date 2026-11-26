using System.ComponentModel.DataAnnotations;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A factory's form over rows that opens with one row ready: <c>.Starting(m =&gt; m.Pistols = [new()])</c>.</summary>
[Route("/ui-operation-form/starting")]
public sealed partial class OperationFormStartingPage : Component
{
    /// <summary>How many pistols the last save was handed.</summary>
    public static int Saved { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Main[
            OperationForms.Of<TankInput>(
                null,
                static _ => Task.FromResult<TankInput?>(new TankInput()),
                static (input, _) =>
                {
                    Saved = input.Pistols.Count;
                    return Task.CompletedTask;
                },
                static () => true)
            .Starting(static m => m.Pistols = [new()])
            .ConfirmLeave("Leave without saving?")[f => [
                Ui.Table[
                    Ui.TableColumns[Ui.TableColumn["Number"]],
                    Ui.TableRows[f.Pistols.Select((row, i) => Ui.TableRow.Key(i)[
                        Ui.TableCell[Ui.Input.Bind(() => row.Number).Id("number-" + i).InputClass("number")]
                    ])]
                ],
                Ui.Button.Id("save").Type(Ui.ButtonType.Submit)["Save"],
            ]]
        ];

    /// <summary>The form's model: a tank's pistols as rows.</summary>
    public sealed class TankInput
    {
        /// <summary>The rows.</summary>
        public List<PistolRow> Pistols { get; set; } = [];
    }

    /// <summary>One pistol.</summary>
    public sealed class PistolRow
    {
        /// <summary>The pistol's number on the pump.</summary>
        [Required(ErrorMessage = "A pistol needs its number.")]
        public string? Number { get; set; }
    }
}

/// <summary>A form's starting model is drawn as it opens, is no edit of the reader's, and a test states it as <c>given</c>.</summary>
[Collection(OperationFormRowsCollection.Name)]
public sealed class UiOperationFormStartingTests
{
    [Fact]
    public void A_factorys_form_opens_with_its_starting_row_and_nothing_marked_changed()
    {
        var page = Page.Visit("/ui-operation-form/starting");

        var rows = page.FindAll("input.number").Count;
        var form = page.Find("form");

        Assert.Equal(1, rows);
        Assert.Equal("Leave without saving?", form.Attribute("data-rask-confirm-leave"));
        Assert.Null(form.Attribute("data-rask-edited"));
    }

    [Fact]
    public async Task The_starting_row_is_saved_with_what_the_reader_typed_into_it()
    {
        var page = Page.Visit("/ui-operation-form/starting");

        await page.On("#number-0").Change("1");
        await page.On("form").Submit();

        Assert.Equal(1, OperationFormStartingPage.Saved);
        Assert.Single(page.FindAll("input.number"));
    }

    [Fact]
    public async Task A_test_states_the_starting_model_as_given_and_the_form_shows_the_rows_rule()
    {
        var shows = Rask.Testing.Test.FormShows<OperationFormStartingPage.TankInput>(
            m => m.Pistols[0].Number, "", given: static m => m.Pistols = [new()], "A pistol needs its number.");

        await shows;

        Assert.True(shows.IsCompletedSuccessfully);
    }
}
