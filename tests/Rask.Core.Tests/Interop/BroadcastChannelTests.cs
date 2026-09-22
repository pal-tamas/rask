using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class BroadcastChannelTests
{
    [Fact]
    public async Task Opening_registers_the_handler_and_opens_the_named_channel()
    {
        var js = new FakeJsRuntime();

        var conn = await new BroadcastChannelService(js).OpenAsync("room", _ => Task.CompletedTask);

        Assert.NotNull(conn);
        // open is called with (id, name); the id is the first arg, the name the second.
        var args = js.ArgsFor("__raskBroadcast.open");
        Assert.IsType<int>(args![0]);
        Assert.Equal("room", args[1]);
    }

    [Fact]
    public async Task A_received_message_is_routed_to_the_registered_handler()
    {
        var js = new FakeJsRuntime();
        string? got = null;
        await new BroadcastChannelService(js).OpenAsync("room", msg =>
        {
            got = msg;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskBroadcast.open")![0]!;

        await BroadcastInterop.Receive(id, "hello");

        Assert.Equal("hello", got);
    }

    [Fact]
    public async Task A_post_sends_the_message_on_the_connection_id()
    {
        var js = new FakeJsRuntime();
        var conn = await new BroadcastChannelService(js).OpenAsync("room", _ => Task.CompletedTask);
        var id = (int)js.ArgsFor("__raskBroadcast.open")![0]!;

        await conn.PostAsync("ping");

        Assert.Equal([id, "ping"], js.ArgsFor("__raskBroadcast.post"));
    }

    [Fact]
    public async Task Disposing_closes_the_channel_and_stops_routing()
    {
        var js = new FakeJsRuntime();
        var received = 0;
        var conn = await new BroadcastChannelService(js).OpenAsync("room", _ =>
        {
            received++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskBroadcast.open")![0]!;

        await conn.DisposeAsync();
        await BroadcastInterop.Receive(id, "after-close"); // unregistered → no-op

        Assert.Equal([id], js.ArgsFor("__raskBroadcast.close"));
        Assert.Equal(0, received);
    }

    [Fact]
    public async Task A_message_for_an_unknown_id_does_nothing()
    {
        await BroadcastInterop.Receive(-12345, "nobody-listening");
    }

    [Fact]
    public async Task Opening_with_null_args_throws()
    {
        var svc = new BroadcastChannelService(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.OpenAsync(null!, _ => Task.CompletedTask));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.OpenAsync("room", null!));
    }
}
