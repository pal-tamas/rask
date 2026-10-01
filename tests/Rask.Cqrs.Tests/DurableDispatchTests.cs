using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

// A real durable handler, so the generator's half is under test too: it records the handler beside its event.
public sealed record Audited(int N) : IEvent;

public sealed class AuditTrail : IDurableHandler<Audited>
{
    public Task Handle(Audited e) => Task.CompletedTask;
}

// The tables are process-global, so the hand-registered cases each own a group key and types nothing else uses.
public sealed class DurableDispatchTests
{
    public sealed record NoStoreEvent(int N) : IEvent;

    public sealed record StoredEvent(int N) : IEvent;

    public sealed record AlreadyStored(int N) : IEvent;

    public sealed record Dropped(int N) : IEvent;

    public sealed class Receipt;

    public sealed class Charge;

    private sealed class RecordingStore : IDurableEventStore
    {
        public List<(IEvent Event, IReadOnlyList<string> Handlers)> Stored { get; } = [];

        public Task Store(IEvent e, IReadOnlyList<string> handlers, CancellationToken cancellationToken)
        {
            Stored.Add((e, handlers));
            return Task.CompletedTask;
        }
    }

    private static CqrsRegistry.EventInvoker Notes(List<string> log, string label) =>
        (_, _, _) =>
        {
            log.Add(label);
            return Task.CompletedTask;
        };

    private static IDispatcher Dispatcher(IDurableEventStore? store = null)
    {
        var services = new ServiceCollection().AddRaskCqrs();
        if (store is not null)
        {
            services.AddSingleton(store);
        }

        return services.BuildServiceProvider().GetRequiredService<IDispatcher>();
    }

    [Fact]
    public async Task Without_a_store_a_durable_handler_runs_in_memory_when_its_event_is_published()
    {
        var log = new List<string>();
        CqrsRegistry.ReplaceDurableHandlers(new object(), [(typeof(NoStoreEvent), typeof(Receipt), Notes(log, "receipt"))]);

        await Dispatcher().Publish(new NoStoreEvent(1), TestContext.Current.CancellationToken);

        Assert.Equal(["receipt"], log);
    }

    [Fact]
    public async Task With_a_store_publishing_hands_it_every_durable_handler_and_runs_none_here()
    {
        var log = new List<string>();
        var store = new RecordingStore();
        CqrsRegistry.ReplaceDurableHandlers(new object(),
        [
            (typeof(StoredEvent), typeof(Receipt), Notes(log, "receipt")),
            (typeof(StoredEvent), typeof(Charge), Notes(log, "charge")),
        ]);

        await Dispatcher(store).Publish(new StoredEvent(1), TestContext.Current.CancellationToken);

        Assert.Empty(log);
        Assert.Equal(2, Assert.Single(store.Stored).Handlers.Count);
    }

    [Fact]
    public async Task An_event_its_save_already_stored_is_not_stored_again_when_published_after_the_commit()
    {
        var store = new RecordingStore();
        CqrsRegistry.ReplaceDurableHandlers(new object(), [(typeof(AlreadyStored), typeof(Receipt), Notes([], "receipt"))]);
        var e = new AlreadyStored(1);
        DurableEvents.MarkStored(e);

        await Dispatcher(store).Publish(e, TestContext.Current.CancellationToken);

        Assert.Empty(store.Stored);
    }

    [Fact]
    public void Replacing_the_durable_group_drops_a_handler_it_no_longer_registers()
    {
        var key = new object();
        CqrsRegistry.ReplaceDurableHandlers(key, [(typeof(Dropped), typeof(Receipt), Notes([], "receipt"))]);
        var name = Assert.Single(CqrsRegistry.DurableHandlersOf(typeof(Dropped)));

        CqrsRegistry.ReplaceDurableHandlers(key, []);

        Assert.Empty(CqrsRegistry.DurableHandlersOf(typeof(Dropped)));
        Assert.Null(CqrsRegistry.FindDurableHandler(name));
    }

    [Fact]
    public void A_generated_durable_handler_is_recorded_beside_its_event()
    {
        var names = CqrsRegistry.DurableHandlersOf(typeof(Audited));

        Assert.Equal($"{CqrsRegistry.NameOf(typeof(AuditTrail))}:{CqrsRegistry.NameOf(typeof(Audited))}", Assert.Single(names));
        Assert.NotNull(CqrsRegistry.FindDurableHandler(names[0]));
        Assert.Equal(typeof(Audited), CqrsRegistry.FindDurableEvent(CqrsRegistry.NameOf(typeof(Audited))));
    }
}
