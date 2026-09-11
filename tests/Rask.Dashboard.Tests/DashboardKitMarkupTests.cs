using Microsoft.Extensions.DependencyInjection;
using Rask.Dashboard.Pages;
using Rask.Jobs;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
///     What the pages draw with the kit: the facts that used to live in hand-written class strings and now
///     live in typed steps — a dead letter's tone, and a queue card that is one link.
/// </summary>
public sealed class DashboardKitMarkupTests
{
    [Fact]
    public async Task A_dead_letter_row_carries_the_error_tone()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SaveAsync(h, new Job
        {
            Type = "Some.Job",
            Payload = "{}",
            RunAt = now.AddHours(-1),
            CreatedAt = now.AddHours(-1),
            Attempts = h.Get<JobOptions>().MaxAttempts,   // out of attempts and unprocessed: a dead letter
            Error = "boom",
        });

        var component = ActivatorUtilities.CreateInstance<QueuePage>(h.Services);
        component.Queue = "jobs";
        component.Show = "failed";
        var page = RaskTest.Render(component, h.Services);
        await page.WaitForAsync("dead letter");

        var row = Assert.Single(page.FindAll("tbody tr"));
        Assert.Contains("bg-error/10", row.Attribute("class") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_queue_card_on_the_overview_is_one_link_to_its_queue()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);

        var component = ActivatorUtilities.CreateInstance<OverviewPage>(h.Services);
        var page = RaskTest.Render(component, h.Services);
        await page.WaitForAsync("Outstanding");

        // One link, holding the figures rather than sitting beside them.
        var card = Assert.Single(page.FindAll("a"), a =>
            (a.Attribute("href") ?? "").EndsWith("/queues/jobs", StringComparison.Ordinal));
        Assert.Contains("Outstanding", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("Failed", card.TextContent, StringComparison.Ordinal);
    }

    private static async Task SaveAsync(DashboardHarness harness, Job job)
    {
        await using var db = harness.NewContext();
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
    }
}
