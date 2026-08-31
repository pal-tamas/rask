using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Outbox.Tests;

/// <summary>
///     Registering the outbox hands it domain-event delivery, with no second argument on
///     <c>AddRaskData</c> and no ordering requirement between the two calls.
/// </summary>
/// <remarks>
///     <para>
///         These pin the fix for a silent durability bug. <c>AddRaskData</c> used to decide whether to
///         register <c>DomainEventInterceptor</c> at the moment it was called, from an option the caller
///         had to remember to set. Forget it — or write the two <c>Add</c> calls in the other order — and
///         the in-process publisher drained and cleared every entity's events in <c>SavingChanges</c>
///         before <c>OutboxInterceptor</c> could copy them. The outbox table stayed empty, delivery
///         silently stopped being durable, and <b>nothing failed</b>, because the handlers still ran
///         in-process. The decision now happens when the container is built, from whether anything
///         registered an <see cref="IDomainEventDeliveryOwner" />.
///     </para>
///     <para>
///         Each test builds its own provider over its own SQLite file, so the arrangement under test is
///         the registration itself rather than a shared fixture's.
///     </para>
/// </remarks>
[Collection(OutboxDbCollection.Name)]
public sealed class OutboxDeliveryHandoverTests : IDisposable
{
    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A test file left behind is not a failure worth failing the run for.
            }
        }
    }

    // Builds a provider whose registration order the caller controls, which is the whole point here.
    private ServiceProvider Build(Action<IServiceCollection, string> register)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-outbox-handover-{Guid.NewGuid():N}.db");
        _paths.Add(path);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Recorder>();
        services.AddRaskCqrs();
        register(services, path);

        var provider = services.BuildServiceProvider();
        using var db = provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
        return provider;
    }

    private static void AddContext(IServiceCollection services, string path) =>
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite($"Data Source={path}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

    private static async Task<Order> PlaceAsync(ServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var order = Order.Place("ada");
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<int> OutboxCountAsync(ServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Set<OutboxMessage>().CountAsync();
    }

    [Fact]
    public async Task A_plain_AddRaskData_hands_delivery_to_the_outbox()
    {
        // The headline: AddRaskData() takes NO argument. Before the fix this arrangement wrote nothing to
        // the outbox at all, and reported no error while doing it.
        await using var provider = Build((services, path) =>
        {
            services.AddRaskData();
            services.AddRaskOutbox<OutboxDbContext>();
            AddContext(services, path);
        });

        await PlaceAsync(provider);

        Assert.Equal(1, await OutboxCountAsync(provider));
        Assert.Empty(provider.GetRequiredService<Recorder>().Events); // the outbox owns delivery, not the interceptor
    }

    [Fact]
    public async Task The_handover_holds_when_the_outbox_is_registered_first()
    {
        // The order-independence claim. Deciding at registration time could not survive this swap: at the
        // moment AddRaskData ran it had no way to know an outbox was already in the collection.
        await using var provider = Build((services, path) =>
        {
            services.AddRaskOutbox<OutboxDbContext>();
            services.AddRaskData();
            AddContext(services, path);
        });

        await PlaceAsync(provider);

        Assert.Equal(1, await OutboxCountAsync(provider));
        Assert.Empty(provider.GetRequiredService<Recorder>().Events);
    }

    [Fact]
    public async Task The_context_factory_may_be_registered_before_the_outbox()
    {
        // The scaffolded Program.cs carried a comment insisting AddRaskOutbox had to precede
        // AddDbContextFactory "so its interceptor is in the container when the factory resolves
        // ISaveChangesInterceptor". It does not: the (sp, o) callback runs when the factory is first
        // resolved, which is after Build(), so it observes every registration whenever it was made.
        //
        // Read this one against A_plain_AddRaskData_hands_delivery_to_the_outbox: the two arrangements
        // differ ONLY in where AddDbContextFactory sits, and both write the message. That pair is what
        // lets the ordering comment be deleted rather than carried forward into the facade.
        await using var provider = Build((services, path) =>
        {
            AddContext(services, path);
            services.AddRaskData();
            services.AddRaskOutbox<OutboxDbContext>();
        });

        await PlaceAsync(provider);

        Assert.Equal(1, await OutboxCountAsync(provider));
    }

    [Fact]
    public async Task An_explicit_true_overrides_the_handover_and_costs_the_outbox_its_copy()
    {
        // The override is honoured in the awkward direction too, and this documents what it buys: the
        // in-process publisher runs FIRST (registration order), draining the events before the outbox
        // interceptor sees them. That is the old bug — now reachable only by asking for it in writing.
        await using var provider = Build((services, path) =>
        {
            services.AddRaskData(o => o.DispatchDomainEventsInProcess = true);
            services.AddRaskOutbox<OutboxDbContext>();
            AddContext(services, path);
        });

        await PlaceAsync(provider);

        Assert.Single(provider.GetRequiredService<Recorder>().Events);
        Assert.Equal(0, await OutboxCountAsync(provider));
    }

    [Fact]
    public async Task Without_an_outbox_events_still_publish_in_process()
    {
        // The regression guard for the default path: no owner registered, so nothing stands down. Without
        // this, "the outbox wins" could be implemented as "the interceptor never runs" and still look green.
        await using var provider = Build((services, path) =>
        {
            services.AddRaskData();
            AddContext(services, path);
        });

        await PlaceAsync(provider);

        Assert.Single(provider.GetRequiredService<Recorder>().Events);
        Assert.Equal(0, await OutboxCountAsync(provider)); // no outbox interceptor registered
    }

    [Fact]
    public async Task An_explicit_false_disables_in_process_dispatch_with_no_outbox()
    {
        // The third state stays meaningful: no owner, but the caller wants nothing published either.
        await using var provider = Build((services, path) =>
        {
            services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);
            AddContext(services, path);
        });

        await PlaceAsync(provider);

        Assert.Empty(provider.GetRequiredService<Recorder>().Events);
    }
}
