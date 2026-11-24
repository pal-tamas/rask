using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

public sealed class EventsFakeTests
{
    private static ServiceProvider Build(Recorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddRaskCqrs();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_published_event_is_recorded_and_no_handler_hears_it()
    {
        var recorder = new Recorder();
        await using var services = Build(recorder);
        using var events = Events.Fake();

        await services.GetRequiredService<IDispatcher>().Publish(new Pinged("p"), TestContext.Current.CancellationToken);

        events.Published<Pinged>().Once();
        Assert.Empty(recorder.Entries);
    }

    [Fact]
    public async Task The_static_publish_is_recorded_with_no_app_to_reach()
    {
        using var events = Events.Fake();

        await Dispatcher.Publish(new Pinged("alone"), TestContext.Current.CancellationToken);

        Assert.Equal("alone", events.Published<Pinged>().Only().Message);
    }

    [Fact]
    public async Task Run_publishes_every_recorded_event_through_its_real_handlers()
    {
        var recorder = new Recorder();
        await using var services = Build(recorder);
        using var events = Events.Fake();
        await services.GetRequiredService<IDispatcher>().Publish(new Pinged("p"), TestContext.Current.CancellationToken);

        await events.Run(TestContext.Current.CancellationToken);

        Assert.Equal(["A:p", "B:p"], recorder.Entries.Order(StringComparer.Ordinal));
        events.Published<Pinged>().Once();
    }

    [Fact]
    public async Task An_event_a_handler_publishes_during_Run_is_recorded_and_published_in_the_same_call()
    {
        var recorder = new Recorder();
        await using var services = Build(recorder);
        using var events = Events.Fake();
        await services.GetRequiredService<IDispatcher>().Publish(new Forwarded("r"), TestContext.Current.CancellationToken);

        await events.Run(TestContext.Current.CancellationToken);

        events.Published<Forwarded>().Once();
        events.Published<Pinged>().Where(e => e.Message == "forwarded r").Once();
        Assert.Contains("A:forwarded r", recorder.Entries);
    }

    [Fact]
    public async Task Where_narrows_and_the_counts_say_how_many_matched()
    {
        await using var services = Build(new Recorder());
        var dispatcher = services.GetRequiredService<IDispatcher>();
        using var events = Events.Fake();

        await dispatcher.Publish(new Pinged("a"), TestContext.Current.CancellationToken);
        await dispatcher.Publish(new Pinged("b"), TestContext.Current.CancellationToken);
        await dispatcher.Publish(new Pinged("a"), TestContext.Current.CancellationToken);

        events.Published<Pinged>().Exactly(3);
        events.Published<Pinged>().Where(e => e.Message == "a").Twice();
        events.Published<Pinged>().Where(e => e.Message == "c").None();
        events.Published<Unheard>().None();
        Assert.Equal(["a", "b", "a"], events.Published<Pinged>().All.Select(e => e.Message));
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task A_failed_expectation_names_what_was_published_instead()
    {
        await using var services = Build(new Recorder());
        using var events = Events.Fake();
        await services.GetRequiredService<IDispatcher>().Publish(new Pinged("p"), TestContext.Current.CancellationToken);

        var failed = Assert.Throws<PublishedEventsException>(() => events.Published<Unheard>().Once());

        Assert.Equal("Expected one Unheard; none was published. Published: Pinged { Message = p }.", failed.Message);
    }

    [Fact]
    public async Task Disposing_the_fake_puts_real_publishing_back()
    {
        var recorder = new Recorder();
        await using var services = Build(recorder);
        var dispatcher = services.GetRequiredService<IDispatcher>();
        var events = Events.Fake();
        await dispatcher.Publish(new Pinged("faked"), TestContext.Current.CancellationToken);

        events.Dispose();
        await dispatcher.Publish(new Pinged("real"), TestContext.Current.CancellationToken);

        Assert.Equal(["A:real", "B:real"], recorder.Entries.Order(StringComparer.Ordinal));
        events.Published<Pinged>().Once();
    }

    [Fact]
    public async Task A_fake_opened_in_another_flow_never_sees_this_flows_events()
    {
        var recorder = new Recorder();
        await using var services = Build(recorder);
        var dispatcher = services.GetRequiredService<IDispatcher>();
        var opened = new TaskCompletionSource<EventsFake>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var other = Task.Run(async () =>
        {
            using var inner = Events.Fake();
            opened.SetResult(inner);
            await release.Task.ConfigureAwait(false);
        }, TestContext.Current.CancellationToken);
        var otherFake = await opened.Task;

        await dispatcher.Publish(new Pinged("here"), TestContext.Current.CancellationToken);
        release.SetResult();
        await other;

        Assert.Equal(["A:here", "B:here"], recorder.Entries.Order(StringComparer.Ordinal));
        otherFake.Published<Pinged>().None();
    }
}

// ---- An event whose handler publishes another ----
public sealed record Forwarded(string Message) : IEvent;

public sealed class ForwardedHandler : IEventHandler<Forwarded>
{
    public Task Handle(Forwarded e) => Dispatcher.Publish(new Pinged($"forwarded {e.Message}"));
}
