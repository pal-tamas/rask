using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wire;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A save the store refuses, in a real browser on a live server page: the message arrives under the fields
///     it names, the reader stays where they were, a correction takes it away and the next save goes through.
/// </summary>
/// <remarks>The session fails the journey on any error the page did not catch, which is half the point.</remarks>
public sealed class FieldFailureTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Taken = "That invoice number is taken.";

    [Fact]
    public async Task A_refused_save_is_told_under_its_fields_and_a_correction_lets_the_next_one_through()
    {
        await using var session = await HookSession.OpenAsync<RefusedSavePage>(playwright);
        var page = session.Page;

        await page.FillAsync("#number", "42");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#number-error")).ToContainTextAsync(Taken);
        await Expect(page.Locator("#year-error")).ToContainTextAsync(Taken);
        await Expect(page.Locator("#number")).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#year")).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=");
        await Expect(page.Locator("#save")).ToBeEnabledAsync();
        await page.FillAsync("#number", "43");
        await Expect(page.Locator("#number-error")).ToBeHiddenAsync();
        await Expect(page.Locator("#year-error")).ToBeHiddenAsync();
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=2026/43");
        await Expect(page.Locator("#number")).Not.ToHaveAttributeAsync("aria-invalid", "true");
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
    }

    [Fact]
    public async Task A_refused_save_inside_a_modal_leaves_it_open_with_the_message_under_the_field()
    {
        await using var session = await HookSession.OpenAsync<RefusedSavePage>(playwright);
        var page = session.Page;
        var dialog = page.Locator("dialog");

        await page.ClickAsync("#rename");
        await Expect(dialog).ToBeVisibleAsync();
        await page.FillAsync("#name", "Budapest");
        await page.ClickAsync("#rename-save");
        await Expect(page.Locator("#name-error")).ToContainTextAsync("That name is in use.");
        var stillModal = await dialog.EvaluateAsync<bool>("d => d.matches(':modal')");
        await page.FillAsync("#name", "Szeged");
        await page.ClickAsync("#rename-save");

        await Expect(page.Locator("#renamed")).ToHaveTextAsync("renamed=Szeged");
        await Expect(dialog).ToBeHiddenAsync();
        Assert.True(stillModal, "the refused save closed the modal it was made in");
    }
}

/// <summary>An invoice whose number can be taken, and a rename in a modal whose name can be in use.</summary>
public sealed partial class RefusedSavePage : Component
{
    private readonly Invoice _invoice = new();
    private readonly Renaming _renaming = new();
    private string _saved = "";
    private string _renamed = "";
    private bool _open;

    protected override Component? HeadAssets => Markup.Title["refused save"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("saved")[$"saved={_saved}"],
        P.Id("renamed")[$"renamed={_renamed}"],
        Form.Model(_invoice).OnSubmit(Save)[
            Ui.Input.Bind(() => _invoice.Year).Label("Year").Id("year"),
            Ui.Input.Bind(() => _invoice.Number).Label("Number").Id("number"),
            Ui.Button.Submit.Id("save")["Save"]
        ],
        Button.Id("rename").OnClick(() => _open = true)["rename"],
        Ui.Modal.Open(_open).OnClose(() => _open = false)[
            Form.Model(_renaming).OnSubmit(Rename)[
                Ui.Input.Bind(() => _renaming.Name).Label("Name").Id("name"),
                Ui.Button.Submit.Id("rename-save")["Rename"]
            ]
        ]
    ];

    private async Task Save(Invoice invoice)
    {
        await Task.Delay(20);
        if (invoice is { Year: "2026", Number: "42" })
        {
            throw new Refused(new FieldFailure("That invoice number is taken.", ["Year", "Number"], Source: "IX_Invoice_Year_Number"));
        }

        _saved = $"{invoice.Year}/{invoice.Number}";
    }

    private void Rename(Renaming renaming)
    {
        if (string.Equals(renaming.Name, "Budapest", StringComparison.Ordinal))
        {
            throw new Refused(new FieldFailure("That name is in use.", ["Name"]));
        }

        _renamed = renaming.Name;
        _open = false;
    }

    private sealed class Invoice
    {
        public string Year { get; set; } = "2026";
        public string Number { get; set; } = "";
    }

    private sealed class Renaming
    {
        public string Name { get; set; } = "";
    }

    private sealed class Refused(params FieldFailure[] failures) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures => failures;
    }
}
