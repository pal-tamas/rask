using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

public sealed class DispatcherTests
{
    private static ServiceProvider Build(Action<CqrsOptions>? configure = null, Recorder? recorder = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder ?? new Recorder());
        services.AddRaskCqrs(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Query_dispatches_to_its_handler()
    {
        await using var sp = Build();
        var result = await sp.GetRequiredService<IDispatcher>().DispatchAsync(new Add(2, 3));
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task Void_command_dispatches_to_its_handler()
    {
        var recorder = new Recorder();
        await using var sp = Build(recorder: recorder);
        await sp.GetRequiredService<IDispatcher>().DispatchAsync(new Poke("hi"));
        Assert.Equal(new[] { "poke:hi" }, recorder.Entries);
    }

    [Fact]
    public async Task Command_with_result_dispatches_to_its_handler()
    {
        await using var sp = Build();
        var length = await sp.GetRequiredService<IDispatcher>().DispatchAsync(new CreateThing("abcd"));
        Assert.Equal(4, length);
    }

    [Fact]
    public async Task Umbrella_dispatcher_covers_query_command_and_publish()
    {
        var recorder = new Recorder();
        await using var sp = Build(recorder: recorder);
        var dispatcher = sp.GetRequiredService<IDispatcher>();

        Assert.Equal(7, await dispatcher.DispatchAsync(new Add(3, 4)));
        await dispatcher.DispatchAsync(new Poke("x"));
        await dispatcher.PublishAsync(new Pinged("p"));

        Assert.Contains("poke:x", recorder.Entries);
        Assert.Contains("A:p", recorder.Entries);
        Assert.Contains("B:p", recorder.Entries);
    }

    [Fact]
    public async Task Publish_runs_every_handler_in_registration_order()
    {
        var recorder = new Recorder();
        await using var sp = Build(recorder: recorder);
        await sp.GetRequiredService<IDispatcher>().PublishAsync(new Pinged("go"));
        Assert.Equal(new[] { "A:go", "B:go" }, recorder.Entries);
    }

    [Fact]
    public async Task Publish_with_no_handlers_is_a_noop()
    {
        await using var sp = Build();
        await sp.GetRequiredService<IDispatcher>().PublishAsync(new Unheard());
    }

    [Fact]
    public async Task Unknown_request_throws_a_clear_error()
    {
        await using var sp = Build();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sp.GetRequiredService<IDispatcher>().DispatchAsync(new Orphan()));
        Assert.Contains("No handler is registered", ex.Message);
        Assert.Contains("Orphan", ex.Message);
    }

    [Fact]
    public async Task Open_generic_behavior_wraps_the_handler()
    {
        var recorder = new Recorder();
        await using var sp = Build(o => o.AddOpenBehavior(typeof(TracingBehavior<,>)), recorder);
        await sp.GetRequiredService<IDispatcher>().DispatchAsync(new Add(1, 1));
        Assert.Equal(new[] { "trace-in:Add", "trace-out:Add" }, recorder.Entries);
    }

    [Fact]
    public async Task Behaviors_run_in_registration_order_outermost_first()
    {
        var recorder = new Recorder();
        await using var sp = Build(o =>
        {
            o.AddOpenBehavior(typeof(TracingBehavior<,>));
            o.AddOpenBehavior(typeof(SecondBehavior<,>));
        }, recorder);

        await sp.GetRequiredService<IDispatcher>().DispatchAsync(new Add(1, 1));

        Assert.Equal(
            new[] { "trace-in:Add", "second-in", "second-out", "trace-out:Add" },
            recorder.Entries);
    }

    [Fact]
    public async Task Closed_behavior_can_short_circuit_the_handler()
    {
        var recorder = new Recorder();
        await using var sp = Build(o => o.AddBehavior<Add, int, ShortCircuitAdd>(), recorder);
        var result = await sp.GetRequiredService<IDispatcher>().DispatchAsync(new Add(2, 2));
        Assert.Equal(999, result);
        Assert.Equal(new[] { "short-circuit" }, recorder.Entries);
    }

    [Fact]
    public async Task WhenAll_strategy_runs_all_notification_handlers()
    {
        var recorder = new Recorder();
        await using var sp = Build(o => o.NotificationPublishStrategy = NotificationPublishStrategy.WhenAll, recorder);
        await sp.GetRequiredService<IDispatcher>().PublishAsync(new Pinged("w"));
        Assert.Equal(2, recorder.Entries.Count);
        Assert.Contains("A:w", recorder.Entries);
        Assert.Contains("B:w", recorder.Entries);
    }

    [Fact]
    public void AddOpenBehavior_rejects_a_non_behavior_type()
    {
        var options = new CqrsOptions();
        Assert.Throws<ArgumentException>(() => options.AddOpenBehavior(typeof(List<>)));
    }

    [Fact]
    public async Task AddRaskCqrs_is_idempotent_behaviors_run_once()
    {
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        // A shared library and the app host both register; the second call must be a no-op.
        services.AddRaskCqrs(o => o.AddOpenBehavior(typeof(TracingBehavior<,>)));
        services.AddRaskCqrs(o => o.AddOpenBehavior(typeof(TracingBehavior<,>)));

        await using var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<IDispatcher>().DispatchAsync(new Add(1, 1));

        // The behavior wrapped the handler exactly once, not twice.
        Assert.Equal(new[] { "trace-in:Add", "trace-out:Add" }, recorder.Entries);
    }

    [Fact]
    public async Task Sequential_publish_stops_on_the_first_handler_failure_and_rethrows()
    {
        var recorder = new Recorder();
        // Default fan-out: Sequential + StopOnFirstNotificationException = true.
        await using var sp = Build(recorder: recorder);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sp.GetRequiredService<IDispatcher>().PublishAsync(new Grumble("x")));

        Assert.StartsWith("boom-", ex.Message); // the handler's own exception, rethrown as-is
        // Halted: the run stopped at the first failure, so not every handler recorded.
        Assert.True(
            recorder.Entries.Count < 3,
            $"Expected the run to halt before all three handlers ran; saw [{string.Join(", ", recorder.Entries)}].");
    }

    [Fact]
    public async Task Sequential_publish_without_stop_runs_all_handlers_and_aggregates_failures()
    {
        var recorder = new Recorder();
        await using var sp = Build(o => o.StopOnFirstNotificationException = false, recorder);

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => sp.GetRequiredService<IDispatcher>().PublishAsync(new Grumble("x")));

        Assert.Equal(2, ex.InnerExceptions.Count); // both throwing handlers surfaced
        Assert.Equal(3, recorder.Entries.Count);   // every handler ran despite the earlier failure
    }

    [Fact]
    public async Task WhenAll_publish_starts_every_handler_then_surfaces_a_failure()
    {
        var recorder = new Recorder();
        await using var sp = Build(o => o.NotificationPublishStrategy = NotificationPublishStrategy.WhenAll, recorder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sp.GetRequiredService<IDispatcher>().PublishAsync(new Grumble("x")));

        Assert.Equal(3, recorder.Entries.Count); // all handlers were started before any faulted
    }
}
