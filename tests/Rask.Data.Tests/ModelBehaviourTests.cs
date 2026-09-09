using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// Behaviour on the model, of the kind an application actually writes: a guard, a state change, an event
// — and one method that has to ask the database something before it can decide.
public sealed class Order : Model<Guid>, ISoftDeletable
{
    private Order() { } // EF materialization

    public string Reference { get; private set; } = "";

    public OrderStatus Status { get; private set; } = OrderStatus.Open;

    public DateTime? CancelledAt { get; private set; }

    public DateTime? DeletedAt { get; private set; }

    public static Order Place(string reference) =>
        new() { Id = Guid.NewGuid(), Reference = reference, Status = OrderStatus.Open };

    public void Ship() => Status = OrderStatus.Shipped;

    /// <summary>Pure behaviour: changes this order and nothing else. No database in sight.</summary>
    public void Cancel(DateTime when)
    {
        if (Status == OrderStatus.Shipped)
        {
            throw new InvalidOperationException("A shipped order cannot be cancelled.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            return; // cancelling twice is not an error, it is a no-op
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = when;
        Raise(new OrderCancelled(Id));
    }

    /// <summary>
    ///     Behaviour that needs the database: an order with a dispatched shipment cannot be cancelled,
    ///     and only the database knows. Saves itself, so the caller has nothing to remember.
    /// </summary>
    public async Task<bool> TryCancelAsync(DateTime when, CancellationToken cancellationToken = default)
    {
        if (await Shipment.AnyAsync(s => s.OrderId == Id && s.Dispatched, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        Cancel(when);
        // `this.` is required: SaveAsync is an extension member, so it needs a receiver even here.
        await this.SaveAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

public enum OrderStatus
{
    Open,
    Shipped,
    Cancelled,
}

public sealed class Shipment : Model<Guid>
{
    private Shipment() { }

    public Guid OrderId { get; private set; }

    public bool Dispatched { get; private set; }

    public static Shipment For(Guid orderId, bool dispatched) =>
        new() { Id = Guid.NewGuid(), OrderId = orderId, Dispatched = dispatched };
}

public sealed record OrderCancelled(Guid Id) : INotification;

// Half of these tests construct no database at all, which is the point: behaviour that only changes the
// model is a plain object, so it is tested like one. The half that reads the database gets a real one in
// a line — no mock of a DbContext, and no assertion about a call that was supposed to have happened.
[Collection(DataDbCollection.Name)]
public sealed class ModelBehaviourTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-behaviour-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    // ---- Pure behaviour: no fixture, no database, no ceremony ----------------------------------

    [Fact]
    public void Cancelling_an_open_order_records_when_and_raises_the_event()
    {
        var order = Order.Place("A-1");

        order.Cancel(Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Now, order.CancelledAt);
        Assert.Contains(order.DomainEvents, e => e is OrderCancelled);
    }

    [Fact]
    public void A_shipped_order_refuses_to_be_cancelled()
    {
        var order = Order.Place("A-2");
        order.Ship();

        var error = Assert.Throws<InvalidOperationException>(() => order.Cancel(Now));

        Assert.Contains("shipped", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void Cancelling_twice_is_a_no_op_rather_than_a_second_event()
    {
        var order = Order.Place("A-3");

        order.Cancel(Now);
        order.Cancel(Now.AddHours(1));

        Assert.Equal(Now, order.CancelledAt);
        Assert.Single(order.DomainEvents);
    }

    // ---- Behaviour that reads the database: one line of fixture --------------------------------

    [Fact]
    public async Task Cancelling_saves_itself_with_no_unit_of_work_around_it()
    {
        await using var database = await StartDatabaseAsync();

        var order = Order.Place("B-1");
        await SeedAsync(order, Shipment.For(order.Id, dispatched: false));

        // No Db.Begin() here: TryCancelAsync saves through SaveAsync, which is its own unit of work.
        Assert.True(await order.TryCancelAsync(Now));

        var reloaded = await Order.FindAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, reloaded!.Status);
        Assert.Equal(Now, reloaded.CancelledAt);
    }

    [Fact]
    public async Task An_order_with_a_dispatched_shipment_refuses_and_changes_nothing()
    {
        await using var database = await StartDatabaseAsync();

        var order = Order.Place("B-2");
        await SeedAsync(order, Shipment.For(order.Id, dispatched: true));

        Assert.False(await order.TryCancelAsync(Now));

        var reloaded = await Order.FindAsync(order.Id);
        Assert.Equal(OrderStatus.Open, reloaded!.Status);
        Assert.Null(reloaded.CancelledAt);
    }

    [Fact]
    public async Task A_model_that_saves_itself_joins_its_callers_transaction_rather_than_committing_it()
    {
        // The rule that makes a self-saving method safe to call from anywhere: inside a unit of work it
        // tracks and waits. A caller who wrapped two orders in one transaction gets one transaction, and
        // abandoning it abandons both — rather than the first having quietly committed itself.
        await using var database = await StartDatabaseAsync();

        var first = Order.Place("C-1");
        var second = Order.Place("C-2");
        await SeedAsync(first);
        await SeedAsync(second);

        await using (var uow = Db.Begin())
        {
            Assert.True(await first.TryCancelAsync(Now));
            Assert.True(await second.TryCancelAsync(Now));

            // Nothing is written yet, even though both models "saved themselves".
            Assert.Equal(2, await Order.CountAsync(o => o.Status == OrderStatus.Open));

            // Disposed without a SaveChangesAsync: the whole unit of work is discarded.
        }

        Assert.Equal(2, await Order.CountAsync(o => o.Status == OrderStatus.Open));
        Assert.Equal(0, await Order.CountAsync(o => o.Status == OrderStatus.Cancelled));
    }

    [Fact]
    public async Task The_callers_commit_writes_everything_the_models_saved()
    {
        await using var database = await StartDatabaseAsync();

        var first = Order.Place("D-1");
        var second = Order.Place("D-2");
        await SeedAsync(first);
        await SeedAsync(second);

        await using (var uow = Db.Begin())
        {
            await first.TryCancelAsync(Now);
            await second.TryCancelAsync(Now);
            await uow.SaveChangesAsync();
        }

        Assert.Equal(2, await Order.CountAsync(o => o.Status == OrderStatus.Cancelled));
    }

    [Fact]
    public async Task The_audit_stamps_come_from_the_clock_the_test_supplies()
    {
        var clock = new FakeClock(new DateTimeOffset(Now, TimeSpan.Zero));
        await using var database = await StartDatabaseAsync(clock);

        var order = Order.Place("B-3");
        await SeedAsync(order);

        var reloaded = await Order.FindAsync(order.Id);

        Assert.Equal(Now, reloaded!.CreatedAt);
    }

    [Fact]
    public async Task Removing_a_soft_deletable_model_stamps_it_rather_than_deleting_the_row()
    {
        await using var database = await StartDatabaseAsync();

        var order = Order.Place("B-4");
        await SeedAsync(order);

        await using (var uow = Db.Begin())
        {
            var tracked = await Order.AsTracking().FirstOrDefaultAsync(o => o.Id == order.Id);
            Order.Remove(tracked!);
            await uow.SaveChangesAsync();
        }

        // The interceptors are wired by the fixture, so the conventions behave as they do in production.
        Assert.Equal(0, await Order.CountAsync());
        Assert.Single(await Order.IgnoreQueryFilters().Where(o => o.Id == order.Id).ToListAsync());
    }

    private Task<TestDatabase> StartDatabaseAsync(TimeProvider? clock = null) =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"), clock);

    private static async Task SeedAsync(Order order, params Shipment[] shipments)
    {
        await using var uow = Db.Begin();
        Order.Add(order);
        Shipment.AddRange(shipments);
        await uow.SaveChangesAsync();
    }
}
