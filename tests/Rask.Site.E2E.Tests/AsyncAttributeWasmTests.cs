using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     .NET 11's async DataAnnotations on the WebAssembly host, through the validation guide's example: an
///     <c>AsyncValidationAttribute</c> on the model and nothing declared on the form. A registered email is told under
///     the field once the lookup answers, a lookup that throws is "could not be completed" and is not subscribed past,
///     and a free email is subscribed. At a desktop and a phone width. The same journeys on the Server host, with the
///     lookup held by the test, are <c>AsyncAttributeTests</c> in <c>Rask.Server.E2E.Tests</c>.
/// </summary>
public sealed class AsyncAttributeWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public Task A_registered_email_is_told_a_failed_lookup_is_never_a_pass_and_a_free_one_is_subscribed(int width, int height) => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(width, height);
        await OpenAsync();
        var email = Page.Locator("#v15-email");
        var form = Page.Locator("form:has(#v15-email)");

        await email.FillAsync("ada@rask.sh");
        await email.BlurAsync();
        await Expect(form.GetByText("ada@rask.sh is already registered.")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await email.FillAsync("down@rask.sh");
        await email.BlurAsync();
        await Expect(form.GetByText("Validation could not be completed.")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await form.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Subscribe" }).ClickAsync();
        await Expect(form.GetByText("Validation could not be completed.")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.GetByText("Subscribed: down@rask.sh")).ToHaveCountAsync(0);
        await email.FillAsync("new@rask.sh");
        await email.BlurAsync();
        await Expect(form.GetByText("Validation could not be completed.")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await form.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Subscribe" }).ClickAsync();

        await Expect(Page.GetByText("Subscribed: new@rask.sh")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(form.GetByText("is already registered")).ToHaveCountAsync(0);
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs + "/guides/forms-validation");
        await WaitForInteractiveAsync();
        var email = Page.Locator("#v15-email");
        await Expect(email).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await email.ScrollIntoViewIfNeededAsync();
    }
}
