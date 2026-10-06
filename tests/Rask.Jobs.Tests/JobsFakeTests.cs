using Microsoft.Extensions.DependencyInjection;
using Rask.Batteries;
using Rask.Data;

namespace Rask.Background.Tests;

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

        await Jobs.Enqueue(new SendWelcome(Guid.Empty), TestContext.Current.CancellationToken);

        jobs.Enqueued<SendWelcome>().Once();
    }

    [Fact]
    public async Task A_job_is_recorded_with_the_delay_it_asked_for()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new ChaseInvoice(7), TestContext.Current.CancellationToken).In(24.Hours);

        jobs.Enqueued<ChaseInvoice>().In(24.Hours).Once();
        jobs.Enqueued<ChaseInvoice>().In(1.Hour).None();
    }

    [Fact]
    public async Task The_job_itself_is_reachable_for_what_the_steps_do_not_cover()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new ChaseInvoice(7), TestContext.Current.CancellationToken);

        var enqueued = jobs.Enqueued<ChaseInvoice>().Only();
        Assert.Equal(7, enqueued.Job.Number);
    }

    [Fact]
    public async Task A_job_is_narrowed_by_what_it_carries()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new ChaseInvoice(7), TestContext.Current.CancellationToken);
        await Jobs.Enqueue(new ChaseInvoice(9), TestContext.Current.CancellationToken);

        jobs.Enqueued<ChaseInvoice>().Twice();
        jobs.Enqueued<ChaseInvoice>().Matching(j => j.Number == 7, "for invoice 7").Once();
    }

    [Fact]
    public async Task A_failure_names_the_kind_of_work_that_was_asked_for_instead()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new ChaseInvoice(7), TestContext.Current.CancellationToken);

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

        await Jobs.Enqueue(new SendWelcome(Guid.Empty), TestContext.Current.CancellationToken);

        // The fake asserts the work was ASKED FOR; the handler has its own test. Nothing dispatches here,
        // so a test cannot accidentally depend on a handler's side effect it never registered.
        Assert.Equal(1, jobs.Count);
    }

    [Fact]
    public async Task Disposing_the_fake_puts_the_real_queue_back()
    {
        using (var jobs = Jobs.Fake())
        {
            await Jobs.Enqueue(new SendWelcome(Guid.Empty), TestContext.Current.CancellationToken);
            jobs.Enqueued<SendWelcome>().Once();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Jobs.Enqueue(new SendWelcome(Guid.Empty), TestContext.Current.CancellationToken));
        Assert.Contains("Inject IJobs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_injected_IJobs_can_be_the_fake_too()
    {
        using var jobs = Jobs.Fake();
        IJobs injected = jobs;

        await injected.Enqueue(new ChaseInvoice(7), TestContext.Current.CancellationToken).In(2.Hours);

        jobs.Enqueued<ChaseInvoice>().In(2.Hours).Once();
    }

    [Fact]
    public async Task Run_sends_a_recorded_job_through_its_handler()
    {
        await using var app = App(out var recorder);
        using var work = Ambient.Enter(app);
        using var jobs = Jobs.Fake();
        await Jobs.Enqueue(new RecordJob("receipt"), TestContext.Current.CancellationToken).In(24.Hours);

        await jobs.Run(TestContext.Current.CancellationToken);

        Assert.Equal(["receipt"], recorder.Values);
    }

    [Fact]
    public async Task A_job_enqueued_by_a_running_handler_runs_in_the_same_Run()
    {
        await using var app = App(out var recorder);
        using var work = Ambient.Enter(app);
        using var jobs = Jobs.Fake();
        await Jobs.Enqueue(new ChainJob("second"), TestContext.Current.CancellationToken);

        await jobs.Run(TestContext.Current.CancellationToken);

        Assert.Equal(["second"], recorder.Values);
        jobs.Enqueued<RecordJob>().Once();
    }

    [Fact]
    public async Task A_job_runs_as_the_user_who_enqueued_it()
    {
        var alice = Guid.NewGuid();
        await using var app = App(out var recorder);
        using var work = Ambient.Enter(app);
        using var jobs = Jobs.Fake();
        using (Current.UseUser(alice))
        {
            await Jobs.Enqueue(new WhoAmIJob("alice"), TestContext.Current.CancellationToken);
        }

        using (Current.UseUser(Guid.NewGuid()))
        {
            await jobs.Run(TestContext.Current.CancellationToken);
        }

        Assert.Equal([$"alice:{alice}"], recorder.Values);
    }

    [Fact]
    public async Task Run_twice_does_not_run_a_job_again()
    {
        await using var app = App(out var recorder);
        using var work = Ambient.Enter(app);
        using var jobs = Jobs.Fake();
        await Jobs.Enqueue(new RecordJob("once"), TestContext.Current.CancellationToken);

        await jobs.Run(TestContext.Current.CancellationToken);
        await jobs.Run(TestContext.Current.CancellationToken);

        Assert.Equal(["once"], recorder.Values);
    }

    [Fact]
    public async Task A_handler_that_throws_lets_its_exception_out_of_Run()
    {
        await using var app = App(out _);
        using var work = Ambient.Enter(app);
        using var jobs = Jobs.Fake();
        await Jobs.Enqueue(new FailingJob(), TestContext.Current.CancellationToken);

        var run = () => jobs.Run(TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(run);
        Assert.Equal("boom", error.Message);
    }

    // The handlers and nothing else: no queue, no processor, no database.
    private static ServiceProvider App(out Recorder recorder)
    {
        recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(new Gate());
        services.AddRaskCqrs();
        return services.BuildServiceProvider();
    }
}

// At file scope, not nested: RASK035 refuses a job the generated registry cannot see, because one that
// cannot be rehydrated dead-letters at runtime — the analyzer is right even for a job only a fake takes.
internal sealed record SendWelcome(Guid UserId) : IJob;

internal sealed record ChaseInvoice(int Number) : IJob;
