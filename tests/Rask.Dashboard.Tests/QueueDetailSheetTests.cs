using Microsoft.Extensions.DependencyInjection;
using Rask.Dashboard.Pages;
using Rask.Jobs;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
/// A row's detail moved out of an inline <c>&lt;tr&gt;</c> and into an overlay sheet, and that move broke
/// the one thing the inline version got for free: a confirmation raised from the detail was visible.
/// <para>
/// The sheet is <c>fixed inset-0 z-50</c> over a backdrop, while the confirmation prompt renders in the
/// page's normal flow. So Delete-from-the-sheet set <c>_pending</c>, left <c>_expanded</c> alone, and put
/// its own question underneath the thing that raised it — an operator tapped Delete and the console did
/// nothing observable. Retry never showed it, because Retry skips confirmation entirely.
/// </para>
/// </summary>
public sealed class QueueDetailSheetTests
{
    [Fact]
    public async Task Deleting_from_the_detail_sheet_closes_it_so_the_confirmation_is_visible()
    {
        await using var h = new DashboardHarness(
            Batteries.Jobs,
            // Delete is the Destructive tier, and it is the only row action that asks first.
            configure: o => o.Actions = RaskDashboardActions.Safe | RaskDashboardActions.Destructive);

        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;
        await SeedDeadLetterAsync(h, now, max);

        var page = await RenderQueueAsync(h);

        // Open the sheet.
        await ClickAsync(page, "Details");
        Assert.True(page.Exists("[role=\"dialog\"]"), "the detail sheet did not open");

        // Ask to delete. This raises a confirmation rather than acting.
        await ClickAsync(page, "Delete");

        // The sheet must be gone, or the prompt below is rendered underneath a full-viewport overlay.
        Assert.False(page.Exists("[role=\"dialog\"]"),
            "the sheet stayed open over its own confirmation — Delete looks like a button that does nothing");
        Assert.Contains("cannot be recovered", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_a_row_does_not_by_itself_confirm_anything()
    {
        // The negative half: opening the sheet must not raise a prompt, or the assertion above would pass
        // for the wrong reason.
        await using var h = new DashboardHarness(
            Batteries.Jobs,
            configure: o => o.Actions = RaskDashboardActions.Safe | RaskDashboardActions.Destructive);

        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;
        await SeedDeadLetterAsync(h, now, max);

        var page = await RenderQueueAsync(h);
        await ClickAsync(page, "Details");

        Assert.True(page.Exists("[role=\"dialog\"]"));
        Assert.DoesNotContain("cannot be recovered", page.Html, StringComparison.Ordinal);
    }

    private static Task SeedDeadLetterAsync(DashboardHarness harness, DateTime now, int maxAttempts)
    {
        var job = new Job
        {
            Type = "Some.Job",
            Payload = "{}",
            RunAt = now.AddHours(-1),
            CreatedAt = now.AddHours(-1),
            Attempts = maxAttempts,      // out of attempts and unprocessed: a dead letter
            Error = "boom",
        };

        return SaveAsync(harness, job);
    }

    private static async Task SaveAsync(DashboardHarness harness, Job job)
    {
        await using var db = harness.NewContext();
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
    }

    private static async Task<RenderedComponent<QueuePage>> RenderQueueAsync(DashboardHarness harness)
    {
        var component = ActivatorUtilities.CreateInstance<QueuePage>(harness.Services);

        // Set by hand what PageBinder would bind from the URL: this renders the page directly rather than
        // through the router, so there is no route to bind from.
        component.Queue = "jobs";
        component.Show = "failed";

        var page = RaskTest.Render(component, harness.Services);

        // PollingPanel loads on an asynchronous mount, so the first render is the placeholder.
        await page.WaitForAsync("Details");
        return page;
    }

    // Found by label rather than by a test-only hook: these are OpsButtons with no distinguishing class,
    // and adding a data-testid to production markup to make a test easier would be the wrong trade.
    private static Task ClickAsync(RenderedComponent<QueuePage> page, string label)
    {
        var button = page.FindAll("button")
            .First(b => b.TextContent.Contains(label, StringComparison.Ordinal));

        var handler = button.Attribute("data-rask-on-click");
        Assert.NotNull(handler);
        return page.InvokeAsync(handler);
    }
}
