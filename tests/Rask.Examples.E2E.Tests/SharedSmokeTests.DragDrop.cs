using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Headless drag-and-drop showcase (/drag-drop): a single-list sortable reorder and a
// multi-column Kanban board, both driven by the DragDrop primitive. The primitive's state
// machine is covered by unit tests (DragDropTests); these cover the end-to-end path that units
// can't reach — native HTML5 drag events firing the C# handlers and the live diff morphing the
// reordered DOM back. HTML5 DnD isn't reliably driven by mouse simulation, so we dispatch the
// drag sequence with a shared DataTransfer (the documented Playwright approach for native DnD).
public abstract partial class SharedSmokeTests
{
    private async Task HtmlDragDropAsync(string sourceSelector, string targetSelector)
    {
        var source = Page.Locator(sourceSelector);
        var target = Page.Locator(targetSelector);
        await source.ScrollIntoViewIfNeededAsync();

        var dataTransfer = await Page.EvaluateHandleAsync("() => new DataTransfer()");
        var init = new Dictionary<string, object>
        {
            ["dataTransfer"] = dataTransfer, ["bubbles"] = true, ["cancelable"] = true
        };

        await source.DispatchEventAsync("dragstart", init);
        await target.DispatchEventAsync("dragover", init);
        await target.DispatchEventAsync("drop", init);
        await source.DispatchEventAsync("dragend", init);
    }

    [Fact]
    public Task DragDrop_SortableList_DropReordersRows() => RunAsync(async () =>
    {
        await NavigateToAsync("/drag-drop");
        await Expect(Page.Locator("#dd-fruit-list .dd-item").First).ToContainTextAsync("Apple",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#dd-fruit-list .dd-item")).ToHaveCountAsync(5,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // Drag "Apple" (slot 0) DOWN onto "Cherry" (slot 2). DragDropMove.ApplyTo is
        // direction-aware: dragging down lands the item *after* the target, so the order becomes
        // Banana, Cherry, Apple, Date, Elderberry.
        await HtmlDragDropAsync("[data-testid='fruit-0']", "[data-testid='fruit-2']");

        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(0)).ToContainTextAsync("Banana",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(1)).ToContainTextAsync("Cherry",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(2)).ToContainTextAsync("Apple",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task DragDrop_SortableList_ReordersBothDirections() => RunAsync(async () =>
    {
        await NavigateToAsync("/drag-drop");
        await Expect(Page.Locator("#dd-fruit-list .dd-item").First).ToContainTextAsync("Apple",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        // Dragging DOWN onto the immediate neighbour used to be a no-op (drop-before == source
        // slot). Apple (0) onto Banana (1) now lands Apple after Banana.
        await HtmlDragDropAsync("[data-testid='fruit-0']", "[data-testid='fruit-1']");
        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(0)).ToContainTextAsync("Banana",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(1)).ToContainTextAsync("Apple",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Now drag UP: the row now at slot 2 ("Cherry") onto slot 0 ("Banana") lands it before
        // Banana, so Cherry becomes the first row.
        await HtmlDragDropAsync("[data-testid='fruit-2']", "[data-testid='fruit-0']");
        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(0)).ToContainTextAsync("Cherry",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(1)).ToContainTextAsync("Banana",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task DragDrop_Kanban_MoveCardBetweenColumns() => RunAsync(async () =>
    {
        await NavigateToAsync("/drag-drop");
        await Expect(Page.Locator("[data-testid='card-2']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // "Done" starts with a single card; "To do" owns card-2 ("Write the primitive").
        await Expect(Page.Locator("[data-testid='col-done'] .dd-card")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // Drag card-2 from "To do" onto the existing card in "Done".
        await HtmlDragDropAsync("[data-testid='card-2']", "[data-testid='card-5']");

        await Expect(Page.Locator("[data-testid='col-done'] .dd-card")).ToHaveCountAsync(2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(Page.Locator("[data-testid='col-done'] [data-testid='card-2']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("[data-testid='col-todo'] [data-testid='card-2']")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task DragDrop_Kanban_DropOntoColumnBody_LandsAtEnd() => RunAsync(async () =>
    {
        await NavigateToAsync("/drag-drop");
        await Expect(Page.Locator("[data-testid='card-4']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid='col-done'] .dd-card")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // Drop onto the column *body* (empty space), not onto a card — the whole column body is
        // the drop-at-end zone, so card-4 lands after the existing card in "Done".
        await HtmlDragDropAsync("[data-testid='card-4']", "[data-testid='col-done']");

        await Expect(Page.Locator("[data-testid='col-done'] .dd-card")).ToHaveCountAsync(2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(Page.Locator("[data-testid='col-done'] .dd-card").Nth(1))
            .ToHaveAttributeAsync("data-testid", "card-4",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 10_000 });
        await Expect(Page.Locator("[data-testid='col-doing'] [data-testid='card-4']")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
    });
}
