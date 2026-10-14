using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>data-rask-offline</c> on <c>&lt;html&gt;</c> on the WebAssembly host: it follows the browser's own
///     online and offline state. The Server host's half, which also follows its socket, is
///     <c>OfflineAndReconnectTests</c> in <c>Rask.Server.E2E.Tests</c>.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class OfflineMarkWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task The_mark_is_on_while_the_browser_is_offline_and_off_when_it_is_back() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs);
        await WaitForInteractiveAsync();
        var html = Page.Locator("html");
        await Expect(html).Not.ToHaveAttributeAsync("data-rask-offline", new System.Text.RegularExpressions.Regex(".*"));

        await Page.Context.SetOfflineAsync(true);
        await Expect(html).ToHaveAttributeAsync("data-rask-offline", "");
        await Page.Context.SetOfflineAsync(false);

        await Expect(html).Not.ToHaveAttributeAsync("data-rask-offline", new System.Text.RegularExpressions.Regex(".*"));
    });
}
