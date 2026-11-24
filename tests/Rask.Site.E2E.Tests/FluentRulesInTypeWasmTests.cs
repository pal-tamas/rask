using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     FluentValidation rules written inside the model's own <c>static Rules(InlineValidator&lt;T&gt;)</c>, through the
///     validation guide's example on the WebAssembly host: the fields are told their rules up front — a maxlength, an
///     email input, a min and a max, the required mark — and the rules refuse an empty name and a bad address before the
///     form saves. At a desktop and a phone width.
/// </summary>
public sealed class FluentRulesInTypeWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public Task The_fields_are_told_their_rules_and_the_rules_refuse_before_the_save(int width, int height) => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(width, height);
        await OpenAsync();
        var form = Page.Locator("#v47-supplier");
        var name = form.GetByLabel("Name");
        var email = form.GetByLabel("E-mail");
        var rating = form.GetByLabel("Rating");

        await Expect(name).ToHaveAttributeAsync("maxlength", "40");
        await Expect(name).ToHaveAttributeAsync("aria-required", "true");
        await Expect(email).ToHaveAttributeAsync("type", "email");
        await Expect(rating).ToHaveAttributeAsync("min", "1");
        await Expect(rating).ToHaveAttributeAsync("max", "5");
        var save = form.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Save supplier" });

        // An address the email input cannot take is the browser's to refuse (a type mismatch; docs/forms-validation.md):
        // its bubble stops the submit, so nothing is saved.
        await email.FillAsync("nowhere");
        await save.ClickAsync();
        Assert.True(await email.EvaluateAsync<bool>("el => el.validity.typeMismatch"));
        await Expect(Page.GetByText("Saved ")).ToHaveCountAsync(0);

        // One it takes, under an empty name: the submit goes, and NotEmpty refuses it under the field.
        await email.FillAsync("orders@acme.test");
        await save.ClickAsync();
        await Expect(form.GetByText("'Name' must not be empty.")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.GetByText("Saved ")).ToHaveCountAsync(0);
        await name.FillAsync("Acme");
        await save.ClickAsync();
        await Expect(Page.GetByText("Saved Acme")).ToBeVisibleAsync(new() { Timeout = 10_000 });
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs + "/guides/forms-validation");
        await WaitForInteractiveAsync();
        var form = Page.Locator("#v47-supplier");
        await Expect(form).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await form.ScrollIntoViewIfNeededAsync();
    }
}
