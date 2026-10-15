using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     A live field is released from its mark on the WebAssembly host, through the data input page's bound field: it
///     never says <c>aria-busy</c> and carries no <c>data-loading</c> once its value is answered. The mark itself, held
///     long enough to be looked at, is <c>FieldBusyMarkTests</c> in <c>Rask.Server.E2E.Tests</c>.
/// </summary>
/// <remarks>
///     A handler on this host answers inside the mark's 200 ms delay, so the mark itself is rarely there to see.
/// </remarks>
[Collection(WasmExampleCollection.Name)]
public sealed class FieldBusyMarkWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task A_live_field_never_says_aria_busy_and_carries_no_mark_once_its_value_is_answered() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ClickSidebar("Data input");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data input", new() { Timeout = 15_000 });
        var field = Page.Locator("[data-testid='ui-bound'] input").First;
        await Expect(field).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await field.ScrollIntoViewIfNeededAsync();
        await field.EvaluateAsync(
            """
            el => { window.__fieldBusy = false;
                new MutationObserver(() => { if (el.hasAttribute('aria-busy')) window.__fieldBusy = true; })
                    .observe(el, { attributes: true, attributeFilter: ['aria-busy'] }); }
            """);

        await field.PressSequentiallyAsync("ada", new() { Delay = 30 });
        await Page.WaitForTimeoutAsync(1500);

        Assert.False(await field.EvaluateAsync<bool>("el => el.hasAttribute('data-loading')"));
        Assert.False(await Page.EvaluateAsync<bool>("() => window.__fieldBusy"));
    });
}
