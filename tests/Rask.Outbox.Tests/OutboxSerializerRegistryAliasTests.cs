namespace Rask.Outbox.Tests;

// Every event with a durable handler is known through CqrsRegistry (its hot-reload swap is tested in Rask.Cqrs.Tests);
// what is left here is the hand-written alias a renamed event needs, and the name both sides agree on.
public sealed class OutboxSerializerRegistryAliasTests
{
    public sealed record Renamed(int N) : IEvent;

    [Fact]
    public void An_old_name_registered_by_hand_reads_its_stored_rows_as_the_renamed_event()
    {
        OutboxSerializerRegistry.RegisterEvent("Shop.Orders.OldName", typeof(Renamed));

        var back = OutboxSerializerRegistry.Deserialize("Shop.Orders.OldName", """{"n":3}""");

        Assert.Equal(new Renamed(3), Assert.IsType<Renamed>(back));
    }

    [Fact]
    public void An_event_with_a_durable_handler_round_trips_under_its_dotted_name()
    {
        var (typeName, payload) = OutboxSerializerRegistry.Serialize(new OuterScope.NestedEvent(7));

        var back = OutboxSerializerRegistry.Deserialize(typeName, payload);

        Assert.DoesNotContain('+', typeName);
        Assert.Equal(new OuterScope.NestedEvent(7), Assert.IsType<OuterScope.NestedEvent>(back));
    }

    [Fact]
    public void A_name_nobody_knows_reads_back_as_nothing()
    {
        var back = OutboxSerializerRegistry.Deserialize("Nobody.Registers.This", "{}");

        Assert.Null(back);
    }
}
