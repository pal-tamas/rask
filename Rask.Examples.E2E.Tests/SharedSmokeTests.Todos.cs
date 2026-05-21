using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Todos showcase: a 3-route CRUD page (/todos, /todos/new, /todos/{id:guid}/edit)
// — the only sample exercising a single Component across multiple [Route]
// attributes plus a [RouteParam] of value type Guid?. Tests cover the list
// view, dialog open/close on route change, validation, and CRUD outcomes.
public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task Todos_List_RendersSeedRowsAndCount() => RunAsync(async () =>
    {
        await NavigateToAsync("/todos");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Expect(Page.Locator(".list-group .list-group-item")).ToHaveCountAsync(2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".text-muted.small").First).ToContainTextAsync("2 items, 1 done",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Todos_OpenAddDialog_FromButton_DialogVisible() => RunAsync(async () =>
    {
        await NavigateToAsync("/todos");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/todos/new$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page.Locator("#todo-title")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Expect(Page.Locator("#todo-title")).ToHaveValueAsync("",
            new LocatorAssertionsToHaveValueOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Todos_AddNewItem_AppearsInList() => RunAsync(async () =>
    {
        await NavigateToAsync("/todos");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        var baseRows = await Page.Locator(".list-group .list-group-item").CountAsync();

        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page.Locator("#todo-title")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Page.Locator("#todo-title").FillAsync("Wire up reconnect");
        await Page.Locator("button:has-text('Add')").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page.Locator(".list-group .list-group-item")).ToHaveCountAsync(baseRows + 1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Expect(Page.Locator(".todo-title", new() { HasTextString = "Wire up reconnect" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Todos_EditExistingItem_TitleEditPersists() => RunAsync(async () =>
    {
        await NavigateToAsync("/todos");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator(".list-group-item button:has(i.bi-pencil)").First.ClickAsync();
        await Expect(Page.Locator("#todo-title")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Expect(Page.Locator("#todo-title")).Not.ToHaveValueAsync("",
            new LocatorAssertionsToHaveValueOptions { Timeout = 5_000 });

        await Page.Locator("#todo-title").FillAsync("Read the new README");
        await Page.Locator("button:has-text('Save')").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page.Locator(".todo-title", new() { HasTextString = "Read the new README" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Todos_EmptyTitleSubmit_ShowsRequiredMessage() => RunAsync(async () =>
    {
        await NavigateToAsync("/todos");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page.Locator("#todo-title")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });

        // Submit with empty title — DataAnnotationsValidator should surface the
        // [Required] error and block navigation to /todos.
        await Page.Locator("button:has-text('Add')").ClickAsync();
        await Expect(Page.Locator(".text-danger.small")).ToContainTextAsync("Title is required",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Todos_CancelDialog_ReturnsToList() => RunAsync(async () =>
    {
        await NavigateToAsync("/todos");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page.Locator("#todo-title")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });

        // Scope the Cancel selector to the dialog — otherwise it matches the
        // sidebar's "Cancellation" entry which also contains the substring "Cancel".
        await Page.Locator("dialog[open] button:has-text('Cancel')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page.Locator("#todo-title")).Not.ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Todos_DeleteItem_RemovesRow() => RunAsync(async () =>
    {
        await NavigateToAsync("/todos");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var beforeRows = await Page.Locator(".list-group .list-group-item").CountAsync();
        await Page.Locator(".list-group-item button:has(i.bi-trash)").First.ClickAsync();

        await Expect(Page.Locator(".list-group .list-group-item")).ToHaveCountAsync(beforeRows - 1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Todos_CheckboxToggle_FlipsCompletedClass() => RunAsync(async () =>
    {
        await NavigateToAsync("/todos");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // The seed list has one incomplete item ("Read the Rask README") and
        // one completed. Find the incomplete row's checkbox and toggle it.
        var firstRowTitle = Page.Locator(".list-group-item .todo-title").First;
        var titleText = await firstRowTitle.InnerTextAsync();
        var firstRowCheckbox = Page.Locator(".list-group-item").First.Locator("input[type='checkbox']");
        await firstRowCheckbox.CheckAsync();
        await Page.WaitForTimeoutAsync(200);
        await Expect(Page.Locator(".todo-title.completed", new() { HasTextString = titleText }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    });
}
