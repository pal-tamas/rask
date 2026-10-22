using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     Flux's phone field on the running site, a browser-WebAssembly app: the engine is a file the build wrote
///     into <c>wwwroot/js</c>, fetched by the page, and the number reaches the bound model with the next action.
/// </summary>
/// <remarks>The engine's every key is held to Flux's on a Server host, in <c>Rask.Server.E2E.Tests/UiPhoneTests</c>.</remarks>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitPhoneTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task A_number_typed_is_formatted_and_saved_as_E164_with_the_next_action() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var field = scope.Locator("[data-ui-phone]").First;
        var tel = field.Locator("input[type=tel]");
        await Expect(field.Locator("[data-ui-phone-ready]")).ToHaveCountAsync(1, new() { Timeout = 30_000 });

        await tel.ClickAsync();
        await Page.Keyboard.TypeAsync("2015550123");
        await scope.Locator("[data-testid='ui-phone-save']").ClickAsync();

        await Expect(tel).ToHaveValueAsync("(201) 555-0123");
        await Expect(scope.Locator("[data-testid='ui-phone-saved']")).ToHaveTextAsync("Saved: +12015550123", new() { Timeout = 15_000 });
    });

    [Fact]
    public Task The_list_of_countries_draws_its_flags_from_the_apps_own_files_and_picks_one() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var field = scope.Locator("[data-ui-phone]").First;
        var button = field.Locator("button[role=combobox]");
        await Expect(field.Locator("[data-ui-phone-ready]")).ToHaveCountAsync(1, new() { Timeout = 30_000 });

        await button.ClickAsync();
        await Page.Keyboard.TypeAsync("hung");
        var row = field.Locator("[role=option]:not([data-hidden])");
        await Expect(row).ToHaveCountAsync(1);
        var flag = row.Locator("img");
        await Expect(flag).ToHaveJSPropertyAsync("complete", true);
        var drawn = await flag.EvaluateAsync<bool>("img => img.naturalWidth > 0");
        await Page.Keyboard.PressAsync("Enter");

        Assert.True(drawn, "the list's flag was not served from wwwroot/flags.");
        await Expect(button).ToHaveAttributeAsync("aria-label", "Change country: Hungary (+36)");
    });

    private async Task<ILocator> OpenAsync()
    {
        await OpenUiPageAsync("phone", "Phone");

        var scope = Page.Locator("[data-testid='ui-phone-bound']");
        await Expect(scope).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await scope.ScrollIntoViewIfNeededAsync();
        return scope;
    }
}
