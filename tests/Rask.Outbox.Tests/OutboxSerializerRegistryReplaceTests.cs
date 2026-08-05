using Rask.Cqrs;

namespace Rask.Outbox.Tests;

// The registry is process-global and the generated module initializer already owns a group in it, so
// every test here drives its own group key. That is the point of the group key: a contributor can only
// ever replace its own set.
//
// The stand-in types are plain INotifications rather than IOutboxEvents on purpose. The generator
// registers every IOutboxEvent in the compilation, so one here would sit in the *generated* group as well
// and could never be dropped by replacing a test-owned one. Deserialize only needs an INotification back.
//
// Kept in lockstep with JobSerializerRegistryReplaceTests — the two registries share a shape and have
// drifted into the same bug together before.
public sealed class OutboxSerializerRegistryReplaceTests
{
    public sealed record Original(int N) : INotification;

    public sealed record Renamed(int N) : INotification;

    public sealed record Other(int N) : INotification;

    private static string Name<T>() => typeof(T).FullName!.Replace('+', '.');

    [Fact]
    public void Replacing_a_group_drops_the_name_it_no_longer_registers()
    {
        // #537. RegisterEvent upserted, so a rename under `rask dev` left BOTH names registered: the new
        // one worked, and the old one kept resolving to a type the generator no longer produced.
        var key = new object();
        OutboxSerializerRegistry.Replace(key, [(Name<Original>(), typeof(Original))]);
        Assert.NotNull(OutboxSerializerRegistry.Deserialize(Name<Original>(), """{"n":1}"""));

        OutboxSerializerRegistry.Replace(key, [(Name<Renamed>(), typeof(Renamed))]);

        Assert.Null(OutboxSerializerRegistry.Deserialize(Name<Original>(), """{"n":1}"""));
        Assert.NotNull(OutboxSerializerRegistry.Deserialize(Name<Renamed>(), """{"n":1}"""));
    }

    [Fact]
    public void Replacing_one_group_leaves_another_groups_entries_alone()
    {
        // Each assembly's generated initializer passes its own registry class as the key, and a hot reload
        // re-runs RefreshAll() for every loaded assembly — so refreshing one must not empty the others.
        var mine = new object();
        var theirs = new object();
        OutboxSerializerRegistry.Replace(theirs, [(Name<Other>(), typeof(Other))]);
        OutboxSerializerRegistry.Replace(mine, [(Name<Original>(), typeof(Original))]);

        OutboxSerializerRegistry.Replace(mine, [(Name<Renamed>(), typeof(Renamed))]);

        Assert.NotNull(OutboxSerializerRegistry.Deserialize(Name<Other>(), """{"n":1}"""));
    }

    [Fact]
    public void A_direct_registration_survives_a_group_replace()
    {
        // RegisterEvent is public and belongs to no group, so a generated refresh must not drop it.
        var key = new object();
        OutboxSerializerRegistry.RegisterEvent("Manual.Event", typeof(Other));
        OutboxSerializerRegistry.Replace(key, [(Name<Original>(), typeof(Original))]);

        OutboxSerializerRegistry.Replace(key, []);

        Assert.NotNull(OutboxSerializerRegistry.Deserialize("Manual.Event", """{"n":1}"""));
    }

    [Fact]
    public void The_generated_group_still_resolves_a_nested_event()
    {
        // The generator keys on Roslyn's dotted display name; Serialize normalizes Type.FullName's '+'.
        // Moving the generated call to Replace must not disturb that agreement — a mismatch never publishes.
        var (typeName, payload) = OutboxSerializerRegistry.Serialize(new OuterScope.NestedEvent(7));

        var rehydrated = OutboxSerializerRegistry.Deserialize(typeName, payload);

        Assert.Equal(
            new OuterScope.NestedEvent(7),
            Assert.IsType<OuterScope.NestedEvent>(rehydrated));
    }

    [Fact]
    public void Deserialize_returns_null_for_a_name_no_contributor_registers()
    {
        Assert.Null(OutboxSerializerRegistry.Deserialize("Nobody.Registers.This", "{}"));
    }

    [Fact]
    public void Replace_rejects_a_null_group_key_or_set()
    {
        Assert.Throws<ArgumentNullException>(() => OutboxSerializerRegistry.Replace(null!, []));
        Assert.Throws<ArgumentNullException>(() => OutboxSerializerRegistry.Replace(new object(), null!));
    }
}
