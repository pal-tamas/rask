using Rask.Batteries;

namespace Rask.Jobs.Tests;

/// <summary>
/// The fake stands in for the whole queue, so there is no database to reach — see the exempt list in
/// <see cref="JobsDbCollectionGuardTests"/>.
/// </summary>
public sealed class JobsFakeTests
{
    [Fact]
    public async Task A_fake_takes_every_enqueue_instead_of_the_real_queue()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new SendWelcome(Guid.Empty));

        jobs.Enqueued<SendWelcome>().Once();
    }

    [Fact]
    public async Task A_job_is_recorded_with_the_delay_it_asked_for()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new ChaseInvoice(7)).In(24.Hours);

        jobs.Enqueued<ChaseInvoice>().In(24.Hours).Once();
        jobs.Enqueued<ChaseInvoice>().In(1.Hour).None();
    }

    [Fact]
    public async Task The_job_itself_is_reachable_for_what_the_steps_do_not_cover()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new ChaseInvoice(7));

        var enqueued = jobs.Enqueued<ChaseInvoice>().Single();
        Assert.Equal(7, enqueued.Job.Number);
    }

    [Fact]
    public async Task A_job_is_narrowed_by_what_it_carries()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new ChaseInvoice(7));
        await Jobs.Enqueue(new ChaseInvoice(9));

        jobs.Enqueued<ChaseInvoice>().Twice();
        jobs.Enqueued<ChaseInvoice>().Matching(j => j.Number == 7, "for invoice 7").Once();
    }

    [Fact]
    public async Task A_failure_names_the_kind_of_work_that_was_asked_for_instead()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new ChaseInvoice(7));

        var error = Assert.Throws<CountingException>(() => jobs.Enqueued<SendWelcome>().Once());
        Assert.Contains("Expected one SendWelcome", error.Message, StringComparison.Ordinal);
        Assert.Contains("Enqueued instead: ChaseInvoice.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_with_nothing_enqueued_says_so()
    {
        using var jobs = Jobs.Fake();

        var error = Assert.Throws<CountingException>(() => jobs.Enqueued<SendWelcome>().Once());

        Assert.Contains("Nothing was enqueued at all.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_recorded_job_is_never_run()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new SendWelcome(Guid.Empty));

        // The fake asserts the work was ASKED FOR; the handler has its own test. Nothing dispatches here,
        // so a test cannot accidentally depend on a handler's side effect it never registered.
        Assert.Equal(1, jobs.Count);
    }

    [Fact]
    public async Task Disposing_the_fake_puts_the_real_queue_back()
    {
        using (var jobs = Jobs.Fake())
        {
            await Jobs.Enqueue(new SendWelcome(Guid.Empty));
            jobs.Enqueued<SendWelcome>().Once();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Jobs.Enqueue(new SendWelcome(Guid.Empty)));
        Assert.Contains("Inject IJobs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_injected_IJobs_can_be_the_fake_too()
    {
        using var jobs = Jobs.Fake();
        IJobs injected = jobs;

        await injected.Enqueue(new ChaseInvoice(7)).In(2.Hours);

        jobs.Enqueued<ChaseInvoice>().In(2.Hours).Once();
    }
}

// At file scope, not nested: RASK035 refuses a job the generated registry cannot see, because one that
// cannot be rehydrated dead-letters at runtime — the analyzer is right even for a job only a fake takes.
internal sealed record SendWelcome(Guid UserId) : IJob;

internal sealed record ChaseInvoice(int Number) : IJob;
