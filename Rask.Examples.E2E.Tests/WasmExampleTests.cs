using System.Text.RegularExpressions;
using Rask.Examples.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

[Collection(WasmExampleCollection.Name)]
public sealed class WasmExampleTests(WasmExampleAppFixture app, PlaywrightFixture pw) : ExampleSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public async Task PageReload_AtNestedRoute_StillResolvesToNestedRoute()
    {
        await Page.GotoAsync("/counter");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Counter");

        await Page.ReloadAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*/counter$"));
        await Expect(Page.Locator("h1"))
            .ToHaveTextAsync("Counter", new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
    }
}
