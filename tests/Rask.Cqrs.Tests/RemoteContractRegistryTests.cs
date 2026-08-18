using System.Text.Json;

namespace Rask.Cqrs.Tests;

// The registry is process-global and keyed by Type and wire name, exactly like the dispatch tables, so
// every test here owns its own group key, its own message types and its own names. Nothing is shared, so
// nothing depends on the order xunit happens to run them in.
public sealed class RemoteContractRegistryTests
{
    public sealed record Listed(int N) : IQuery<int>;

    public sealed record Dropped(int N) : IQuery<int>;

    public sealed record Added(int N) : IQuery<int>;

    public sealed record FirstClaimant(int N) : IQuery<int>;

    public sealed record SecondClaimant(int N) : IQuery<int>;

    public sealed record RegisteredAfterAConflict(int N) : IQuery<int>;

    [Fact]
    public void A_registered_contract_is_reachable_by_type_and_by_wire_name()
    {
        RemoteContractRegistry.Replace(new object(), [Contract<Listed>("tests.listed")]);

        Assert.True(RemoteContractRegistry.TryGet(typeof(Listed), out var byType));
        Assert.True(RemoteContractRegistry.TryGet("tests.listed", out var byName));
        Assert.Same(byType, byName);
        Assert.Equal(RemoteMessageKind.Query, byType!.Kind);
        Assert.Equal(typeof(int), byType.ResultType);
    }

    [Fact]
    public void An_unknown_name_is_reported_as_missing_rather_than_throwing()
    {
        Assert.False(RemoteContractRegistry.TryGet("tests.never-registered", out var contract));
        Assert.Null(contract);
    }

    [Fact]
    public void Replacing_a_group_removes_what_that_group_no_longer_declares()
    {
        var group = new object();
        RemoteContractRegistry.Replace(group, [Contract<Dropped>("tests.dropped"), Contract<Added>("tests.added")]);

        Assert.True(RemoteContractRegistry.TryGet(typeof(Dropped), out _));

        RemoteContractRegistry.Replace(group, [Contract<Added>("tests.added")]);

        // The point of Replace over a merge: deleting the last reference to a message stops it being
        // dispatchable, instead of leaving a codec built from IL that no longer exists.
        Assert.False(RemoteContractRegistry.TryGet(typeof(Dropped), out _));
        Assert.False(RemoteContractRegistry.TryGet("tests.dropped", out _));
        Assert.True(RemoteContractRegistry.TryGet(typeof(Added), out _));
    }

    [Fact]
    public void All_lists_what_is_registered()
    {
        RemoteContractRegistry.Replace(new object(), [Contract<Listed>("tests.listed.all")]);

        Assert.Contains(RemoteContractRegistry.All, c => c.Name == "tests.listed.all");
    }

    [Fact]
    public void Two_messages_claiming_one_wire_name_is_rejected_and_leaves_the_registry_usable()
    {
        RemoteContractRegistry.Replace(new object(), [Contract<FirstClaimant>("tests.contested")]);

        var conflicting = new object();
        var error = Assert.Throws<InvalidOperationException>(() =>
            RemoteContractRegistry.Replace(conflicting, [Contract<SecondClaimant>("tests.contested")]));

        Assert.Contains("tests.contested", error.Message, StringComparison.Ordinal);

        // The rejected group must not linger: every later Replace rebuilds from the whole group list, so a
        // poisoned entry would make unrelated registrations throw forever afterwards.
        RemoteContractRegistry.Replace(new object(), [Contract<RegisteredAfterAConflict>("tests.after-conflict")]);

        Assert.True(RemoteContractRegistry.TryGet(typeof(RegisteredAfterAConflict), out _));
        Assert.True(RemoteContractRegistry.TryGet(typeof(FirstClaimant), out var kept));
        Assert.Equal(typeof(FirstClaimant), kept!.MessageType);
    }

    [Fact]
    public void A_group_may_re_register_the_same_name_it_already_owns()
    {
        var group = new object();
        RemoteContractRegistry.Replace(group, [Contract<Listed>("tests.reregistered")]);

        // Same type, same name, new instance — a hot-reload round-trip. Not a conflict with itself.
        RemoteContractRegistry.Replace(group, [Contract<Listed>("tests.reregistered")]);

        Assert.True(RemoteContractRegistry.TryGet("tests.reregistered", out _));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => RemoteContractRegistry.Replace(null!, []));
        Assert.Throws<ArgumentNullException>(() => RemoteContractRegistry.Replace(new object(), null!));
        Assert.Throws<ArgumentNullException>(() => RemoteContractRegistry.TryGet((string)null!, out _));
    }

    private static RemoteContract Contract<TMessage>(string name) => new()
    {
        MessageType = typeof(TMessage),
        Name = name,
        Kind = RemoteMessageKind.Query,
        ResultType = typeof(int),
        WriteMessage = static (writer, _, _) => writer.WriteStartObject(),
        ReadMessage = static (ref Utf8JsonReader _, IReadOnlyList<RemoteFile> _) => new object(),
    };
}
