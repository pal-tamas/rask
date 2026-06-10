using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Deep-link Todos coverage — paths that StandaloneWasm can't reach because it
// lacks SPA fallback. The dialog open/close behaviour is driven by the URL,
// so deep-linking to /todos/new or /todos/{id}/edit must open the dialog on
// first paint.
public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task Todos_DeepLink_NewRoute_OpensDialogDirectly() => RunAsync(async () =>
    {
        await Page.GotoAsync("/todos/new");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#todo-title")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("h5")).ToContainTextAsync("Add todo",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Todos_DeepLink_EditUnknownGuid_ShowsListWithoutDialog() => RunAsync(async () =>
    {
        // RouteParam Guid? binds an unknown id; EditingItem is null so no
        // dialog opens; the list is shown unchanged.
        await Page.GotoAsync($"/todos/{Guid.NewGuid()}/edit");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#todo-title")).Not.ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Expect(Page.Locator(".list-group .list-group-item")).ToHaveCountAsync(2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Todos_BrowserBack_FromDialog_ClosesDialog() => RunAsync(async () =>
    {
        await Page.GotoAsync("/todos");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page.Locator("#todo-title")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });

        await Page.GoBackAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page.Locator("#todo-title")).Not.ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    });
}
