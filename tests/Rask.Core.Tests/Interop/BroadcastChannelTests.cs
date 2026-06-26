using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class BroadcastChannelTests
{
    [Fact]
    public async Task Open_RegistersHandler_AndOpensNamedChannel()
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
    public async Task Receive_RoutesMessage_ToTheRegisteredHandler()
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
    public async Task Post_SendsMessage_OnTheConnectionId()
    {
        var js = new FakeJsRuntime();
        var conn = await new BroadcastChannelService(js).OpenAsync("room", _ => Task.CompletedTask);
        var id = (int)js.ArgsFor("__raskBroadcast.open")![0]!;

        await conn.PostAsync("ping");

        Assert.Equal([id, "ping"], js.ArgsFor("__raskBroadcast.post"));
    }

    [Fact]
    public async Task Dispose_ClosesChannel_AndStopsRouting()
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
    public async Task Receive_UnknownId_IsNoOp()
    {
        await BroadcastInterop.Receive(-12345, "nobody-listening");
    }

    [Fact]
    public async Task Open_NullArgs_Throw()
    {
        var svc = new BroadcastChannelService(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.OpenAsync(null!, _ => Task.CompletedTask));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.OpenAsync("room", null!));
    }
}
