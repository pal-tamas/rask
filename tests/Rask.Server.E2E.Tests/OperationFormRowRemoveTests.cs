using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A form over an operation that takes rows, in a real browser: two rows typed into and the first taken away, the
///     row that stays still shows what was typed into it, not what the removed one held.
/// </summary>
/// <remarks>
///     An app's own suite checked this of its form after the input left behind showed the removed row's text. The rows
///     are keyed by their place, as the generated form's rows are written, and by an identity of their own.
/// </remarks>
public sealed class OperationFormRowRemoveTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData("place")]
    [InlineData("identity")]
    public async Task Removing_the_first_of_two_typed_rows_leaves_the_second_rows_text_in_the_input_that_stays(string keyed)
    {
        OperationFormRowRemovePage.Keyed = keyed;
        await using var session = await HookSession.OpenAsync<OperationFormRowRemovePage>(playwright);
        var page = session.Page;
        var plates = page.Locator("input.plate");
        await page.ClickAsync("#add-truck");
        await Expect(plates).ToHaveCountAsync(1);
        await page.ClickAsync("#add-truck");
        await Expect(plates).ToHaveCountAsync(2);
        await plates.Nth(0).FillAsync("AAA-111");
        await plates.Nth(1).FillAsync("BBB-222");
        await page.Keyboard.PressAsync("Tab");

        await page.Locator("[data-remove]").First.ClickAsync();

        await Expect(page.Locator("#said")).ToHaveTextAsync("BBB-222");
        await Expect(plates).ToHaveCountAsync(1);
        await Expect(plates.First).ToHaveValueAsync("BBB-222");
        Assert.Empty(session.PageErrors);
    }
}

/// <summary>A form over an operation whose model holds a list of rows, each row a plate, added and removed in the form.</summary>
public sealed partial class OperationFormRowRemovePage : Component
{
    private static volatile string _keyed = "place";

    /// <summary>"place" to key a row by its index, "identity" by an id of its own.</summary>
    public static string Keyed
    {
        get => _keyed;
        set => _keyed = value;
    }

    protected override Component? HeadAssets => Markup.Title["operation form rows"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Main[
        OperationForms.Of<TrucksInput>(
            "fleet",
            static _ => Task.FromResult<TrucksInput?>(new TrucksInput()),
            static (_, _) => Task.CompletedTask,
            static () => true)[f => [
                P.Id("said")[string.Join(',', f.Trucks.Select(static t => t.Plate))],
                Ui.Table[
                    Ui.TableColumns[Ui.TableColumn["Plate"], Ui.TableColumn[""]],
                    Ui.TableRows[f.Trucks.Select((row, i) => Ui.TableRow.Key(Keyed == "place" ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : row.Id)[
                        Ui.TableCell[Ui.Input.Bind(() => row.Plate).Id("plate-" + i).InputClass("plate")],
                        Ui.TableCell[Ui.Button.Ghost.Sm.Data("remove", "").OnClick(() => f.Trucks.Remove(row))["Remove"]]
                    ])]
                ],
                Ui.Button.Id("add-truck").OnClick(() => f.Trucks.Add(new()))["Add a truck"],
            ]]
    ];

    /// <summary>The form's model: one list of rows.</summary>
    public sealed class TrucksInput
    {
        /// <summary>The rows.</summary>
        public List<TruckLine> Trucks { get; set; } = [];
    }

    /// <summary>One row.</summary>
    public sealed class TruckLine
    {
        /// <summary>The row's own identity, for a key that follows it.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>The truck's plate.</summary>
        public string Plate { get; set; } = "";
    }
}
