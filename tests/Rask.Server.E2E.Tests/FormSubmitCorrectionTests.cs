using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a one-form test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A field the page calls wrong while its submit is still in flight takes the reader's correction at once.
/// </summary>
/// <remarks>
///     A submit holds its form's fields readonly until it is answered (<c>FormSubmitInFlightTests</c>). A rule that
///     awaits — a lookup — lets the page draw what it already knows at that await, and a field it asks to have
///     corrected (<c>data-rask-on-edit</c>) is handed back then: the key that begins the correction is not dropped.
///     The lookup is held by the test, so the window is as wide as it needs to be, every run.
/// </remarks>
public sealed class FormSubmitCorrectionTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_field_shown_wrong_while_the_submit_awaits_a_lookup_takes_the_first_key_of_its_correction()
    {
        CorrectionFormPage.Lookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await HookSession.OpenAsync<CorrectionFormPage>(playwright);
        var page = session.Page;
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");
        await page.Locator("#travellers").PressSequentiallyAsync("1.5");

        await page.Locator("#travellers").PressAsync("Enter");
        await Expect(page.Locator("#travellers-error")).ToHaveTextAsync("Enter a whole number.");
        await page.Locator("#travellers").PressAsync("Backspace");

        await Expect(page.Locator("#travellers")).ToBeEditableAsync();
        await Expect(page.Locator("#travellers")).Not.ToHaveValueAsync("1.5");
        await Expect(page.Locator("#save")).ToBeDisabledAsync();
        CorrectionFormPage.Lookup.SetResult();
        await Expect(page.Locator("#travellers-error")).ToBeHiddenAsync();
        await Expect(page.Locator("#save")).ToBeEnabledAsync();
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=0");
    }
}

/// <summary>A whole-number field beside one whose rule awaits a lookup the test lets go of.</summary>
public sealed partial class CorrectionFormPage : Component
{
    private readonly CorrectionModel _model = new();
    private bool _ready;
    private int _saves;

    /// <summary>The lookup the destination's rule waits on.</summary>
    internal static TaskCompletionSource Lookup { get; set; } = new();

    protected override Component? HeadAssets => Markup.Title["correction"];
    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        P.Id("is-ready")[_ready ? "ready" : "not yet"],
        P.Id("saves")[$"saves={_saves}"],
        Form.Model(_model).OnSubmit(_ => _saves++)[
            Ui.Input.Bind(() => _model.Name).Label("Destination").Id("name").Validate(NameIsFree),
            Ui.Input.Bind(() => _model.Travellers).Label("Travellers").Id("travellers"),
            Ui.Button.Primary.Submit.Id("save")["Save"]
        ]
    ];

    private static async ValueTask<IEnumerable<string>> NameIsFree(string name)
    {
        await Lookup.Task;
        return [];
    }
}

internal sealed class CorrectionModel
{
    public string Name { get; set; } = "";

    public int? Travellers { get; set; }
}
