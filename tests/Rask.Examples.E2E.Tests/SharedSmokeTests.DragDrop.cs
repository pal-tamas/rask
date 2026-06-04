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
            ["dataTransfer"] = dataTransfer,
            ["bubbles"] = true,
            ["cancelable"] = true
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

        // Drag "Apple" (slot 0) onto "Cherry" (slot 2). ReorderFruit inserts the dragged item
        // before the drop target, so the order becomes Banana, Apple, Cherry, Date, Elderberry.
        await HtmlDragDropAsync("[data-testid='fruit-0']", "[data-testid='fruit-2']");

        await Expect(Page.Locator("#dd-fruit-list .dd-item").First).ToContainTextAsync("Banana",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(1)).ToContainTextAsync("Apple",
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
}
