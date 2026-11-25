using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Background.Tests;

public sealed record FileQueued(Guid File, string Partner) : IEvent;

public sealed record FileChecked(Guid File) : IEvent;

/// <summary>An aggregate whose <c>On(FileQueued)</c> its <c>Configure</c> runs as a job, and whose <c>On(FileChecked)</c> it does not.</summary>
public sealed partial class QueuedFile : Aggregate<Guid>
{
    internal static ConcurrentQueue<string> Ran { get; } = new();

    private QueuedFile() { }

    public static void Configure(EntityTypeBuilder<QueuedFile> b) =>
        b.Job<FileQueued>().InTenantLane("loads").UnlessWaiting(e => e.File.ToString());

    static Task On(FileQueued e)
    {
        Ran.Enqueue($"queued:{e.File}");
        return Task.CompletedTask;
    }

    static Task On(FileChecked e)
    {
        Ran.Enqueue($"checked:{e.File}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     F-27: <c>b.Job&lt;E&gt;()</c> in an aggregate's <c>Configure</c> runs its <c>static Task On(E e)</c> as a job — the
///     durable handler only enqueues it, in the lane and under the key the declaration reads off the event.
/// </summary>
public sealed class EventJobTests
{
    private static readonly Guid Acme = Guid.Parse("0f8b6f1e-1a59-4d0e-9a3a-4c1f7b2f0a01");

    static EventJobTests() => QueuedFile.Configure(new ModelBuilder().Entity<QueuedFile>());

    [Fact]
    public async Task The_handler_of_a_declared_event_is_enqueued_in_the_tenants_lane_and_runs_as_the_job()
    {
        await using var app = App();
        using var work = Ambient.Enter(app);
        using var jobs = Jobs.Fake();
        var file = Guid.NewGuid();

        using (Tenant.Use(Acme))
        {
            await Handle(app, new FileQueued(file, "mol"));
        }

        var enqueued = jobs.Enqueued<AggregateEventJob>().Only();
        Assert.DoesNotContain($"queued:{file}", QueuedFile.Ran);
        Assert.Equal($"loads/{Acme:N}", enqueued.Lane);
        await jobs.Run(TestContext.Current.CancellationToken);
        Assert.Contains($"queued:{file}", QueuedFile.Ran);
    }

    [Fact]
    public async Task A_second_event_for_the_same_key_waits_for_nothing()
    {
        await using var app = App();
        using var work = Ambient.Enter(app);
        using var jobs = Jobs.Fake();
        var file = Guid.NewGuid();

        using (Tenant.Use(Acme))
        {
            await Handle(app, new FileQueued(file, "mol"));
            await Handle(app, new FileQueued(file, "omv"));
        }

        jobs.Enqueued<AggregateEventJob>().Once();
    }

    [Fact]
    public async Task An_event_with_no_declared_job_runs_its_handler_where_the_outbox_runs_it()
    {
        await using var app = App();
        using var work = Ambient.Enter(app);
        using var jobs = Jobs.Fake();
        var file = Guid.NewGuid();

        await Handle(app, new FileChecked(file));

        Assert.Equal(0, jobs.Count);
        Assert.Contains($"checked:{file}", QueuedFile.Ran);
    }

    // What the outbox does with the row the generated handler got: run it by its stored name.
    private static Task Handle(IServiceProvider app, IEvent e) =>
        CqrsRegistry.FindDurableHandler($"{CqrsRegistry.NameOf(typeof(QueuedFile))}:{CqrsRegistry.NameOf(e.GetType())}")!(
            app, e, TestContext.Current.CancellationToken);

    private static ServiceProvider App()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddSingleton<IAggregateEventRoute, AggregateEventJobRoute>();
        AggregateEventJob.Install();
        return services.BuildServiceProvider();
    }
}
