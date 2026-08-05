using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

// The dispatch tables are process-global and the generated module initializer already owns a group in
// them, so every test here drives its own group key with types that have no generated handler of their
// own. Everything is asserted through the public dispatch surface — Rask.Cqrs exposes no internals.
public sealed class CqrsRegistryReplaceTests
{
    // A type per test rather than shared ones: the tables are keyed by Type and live for the process, so
    // two tests registering the same type under different group keys would depend on registration order.
    public sealed record Kept(int N) : ICommand;

    public sealed record Removed(int N) : ICommand;

    public sealed record Noticed(int N) : INotification;

    public sealed record OtherGroupKept(int N) : ICommand;

    public sealed record OtherGroupRemoved(int N) : ICommand;

    public sealed record Orphaned(int N) : ICommand;

    private static IDispatcher Dispatcher() =>
        new ServiceCollection().AddRaskCqrs().BuildServiceProvider().GetRequiredService<IDispatcher>();

    private static CqrsRegistry.RequestInvoker Records(List<string> log, string label) =>
        (_, _, _) =>
        {
            log.Add(label);
            return Task.FromResult(Unit.Value);
        };

    private static CqrsRegistry.NotificationInvoker Notes(List<string> log, string label) =>
        (_, _, _) =>
        {
            log.Add(label);
            return Task.CompletedTask;
        };

    [Fact]
    public async Task Replacing_a_group_drops_a_request_it_no_longer_registers()
    {
        // The sibling of #537 in the two serializer registries. RegisterRequest upserted, so deleting the
        // last handler for a command under `rask dev` left its invoker behind — dispatch kept succeeding
        // through IL that no longer had a handler, instead of reporting that nothing handles it.
        var log = new List<string>();
        var key = new object();
        CqrsRegistry.ReplaceRequests(key, [(typeof(Kept), Records(log, "kept")), (typeof(Removed), Records(log, "removed"))]);
        await Dispatcher().DispatchAsync(new Removed(1));
        Assert.Equal(["removed"], log);

        CqrsRegistry.ReplaceRequests(key, [(typeof(Kept), Records(log, "kept"))]);

        await Dispatcher().DispatchAsync(new Kept(1));
        Assert.Equal(["removed", "kept"], log);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Dispatcher().DispatchAsync(new Removed(1)));
    }

    [Fact]
    public async Task Replacing_a_group_drops_a_notification_it_no_longer_registers()
    {
        // A notification with no invoker is a silent no-op, so a stale one is the worse failure: the fan-out
        // keeps running handlers that were deleted.
        var log = new List<string>();
        var key = new object();
        CqrsRegistry.ReplaceNotifications(key, [(typeof(Noticed), Notes(log, "noticed"))]);
        await Dispatcher().PublishAsync(new Noticed(1));
        Assert.Equal(["noticed"], log);

        CqrsRegistry.ReplaceNotifications(key, []);

        await Dispatcher().PublishAsync(new Noticed(1));
        Assert.Equal(["noticed"], log); // no second entry: the deleted handler is gone
    }

    [Fact]
    public async Task Replacing_one_group_leaves_another_groups_entries_alone()
    {
        // A hot reload re-runs RefreshAll() for every loaded assembly, so refreshing one contributor must
        // not empty the others' dispatch tables.
        var log = new List<string>();
        var mine = new object();
        var theirs = new object();
        CqrsRegistry.ReplaceRequests(theirs, [(typeof(OtherGroupKept), Records(log, "theirs"))]);
        CqrsRegistry.ReplaceRequests(mine, [(typeof(OtherGroupRemoved), Records(log, "mine"))]);

        CqrsRegistry.ReplaceRequests(mine, []);

        await Dispatcher().DispatchAsync(new OtherGroupKept(1));
        Assert.Equal(["theirs"], log);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Dispatcher().DispatchAsync(new OtherGroupRemoved(1)));
    }

    [Fact]
    public async Task A_dropped_request_reports_the_type_that_has_no_handler()
    {
        var key = new object();
        CqrsRegistry.ReplaceRequests(key, [(typeof(Orphaned), Records([], "x"))]);
        CqrsRegistry.ReplaceRequests(key, []);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Dispatcher().DispatchAsync(new Orphaned(1)));

        Assert.Contains(nameof(Orphaned), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_rejects_a_null_group_key_or_set()
    {
        Assert.Throws<ArgumentNullException>(() => CqrsRegistry.ReplaceRequests(null!, []));
        Assert.Throws<ArgumentNullException>(() => CqrsRegistry.ReplaceRequests(new object(), null!));
        Assert.Throws<ArgumentNullException>(() => CqrsRegistry.ReplaceNotifications(null!, []));
        Assert.Throws<ArgumentNullException>(() => CqrsRegistry.ReplaceNotifications(new object(), null!));
    }
}
