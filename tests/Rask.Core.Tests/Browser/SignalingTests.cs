using Rask.Core.Browser;
using Rask.Core.Tests.Interop;

namespace Rask.Core.Tests.Browser;

public class SignalingTests
{
    [Fact]
    public async Task JoinAsync_OpensTheSocketThenSendsAJoinFrame()
    {
        var js = new FakeJsRuntime();

        await new Signaling(js).JoinAsync("room-1", new SignalingHandlers());

        var open = js.ArgsFor("__raskSignal.open")!;
        Assert.IsType<int>(open[0]);
        Assert.Equal("/rask/signaling", open[1]);

        // Order matters: the join rides the socket, so opening has to come first.
        Assert.Equal("__raskSignal.open", js.Calls[0].Identifier);
        Assert.Equal("__raskSignal.send", js.Calls[1].Identifier);
        Assert.Equal("""{"type":"join","room":"room-1"}""", js.ArgsFor("__raskSignal.send")![1]);
    }

    [Fact]
    public async Task JoinAsync_HonoursACustomPath()
    {
        var js = new FakeJsRuntime();

        await new Signaling(js).JoinAsync("room", new SignalingHandlers(), "/custom/signal");

        Assert.Equal("/custom/signal", js.ArgsFor("__raskSignal.open")![1]);
    }

    [Fact]
    public async Task JoinAsync_UnregistersWhenTheSocketFailsToOpen()
    {
        var js = new FakeJsRuntime();
        js.SetException("__raskSignal.open", new InvalidOperationException("refused"));
        var fired = false;
        var handlers = new SignalingHandlers
        {
            OnPeerJoined = _ =>
            {
                fired = true;
                return Task.CompletedTask;
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new Signaling(js).JoinAsync("room", handlers).AsTask());

        for (var id = 1; id <= 64; id++)
        {
            await SignalingInterop.Message(id, "peer-joined", "someone", "");
        }

        Assert.False(fired);
    }

    [Fact]
    public async Task SendAsync_AddressesOnePeer()
    {
        var js = new FakeJsRuntime();
        var connection = await new Signaling(js).JoinAsync("room", new SignalingHandlers());

        await connection.SendAsync("peer-9", "an-offer");

        var sends = js.Calls.Where(c => c.Identifier == "__raskSignal.send").ToArray();
        Assert.Equal("""{"type":"signal","to":"peer-9","payload":"an-offer"}""", sends[1].Args![1]);
    }

    [Fact]
    public async Task OnJoined_CarriesOurIdAndThePeersAlreadyThere()
    {
        var js = new FakeJsRuntime();
        string? self = null;
        IReadOnlyList<string> peers = [];

        await new Signaling(js).JoinAsync("room", new SignalingHandlers
        {
            OnJoined = (id, existing) =>
            {
                self = id;
                peers = existing;
                return Task.CompletedTask;
            }
        });
        var connectionId = (int)js.ArgsFor("__raskSignal.open")![0]!;

        await SignalingInterop.Message(connectionId, "joined", "me", """["a","b"]""");

        Assert.Equal("me", self);
        Assert.Equal(["a", "b"], peers);
    }

    [Fact]
    public async Task OnJoined_SurvivesAMalformedPeerList()
    {
        // The list is parsed, so a relay that ever sent something else must not take the app down.
        var js = new FakeJsRuntime();
        IReadOnlyList<string>? peers = null;

        await new Signaling(js).JoinAsync("room", new SignalingHandlers
        {
            OnJoined = (_, existing) =>
            {
                peers = existing;
                return Task.CompletedTask;
            }
        });
        var id = (int)js.ArgsFor("__raskSignal.open")![0]!;

        await SignalingInterop.Message(id, "joined", "me", "not-json");

        Assert.Empty(peers!);
    }

    [Fact]
    public async Task EveryRelayMessage_ReachesItsOwnCallback()
    {
        var js = new FakeJsRuntime();
        string? joined = null, left = null, error = null;
        (string From, string Payload)? signal = null;

        await new Signaling(js).JoinAsync("room", new SignalingHandlers
        {
            OnPeerJoined = p => { joined = p; return Task.CompletedTask; },
            OnPeerLeft = p => { left = p; return Task.CompletedTask; },
            OnSignal = (from, payload) => { signal = (from, payload); return Task.CompletedTask; },
            OnError = m => { error = m; return Task.CompletedTask; }
        });
        var id = (int)js.ArgsFor("__raskSignal.open")![0]!;

        await SignalingInterop.Message(id, "peer-joined", "p1", "");
        await SignalingInterop.Message(id, "peer-left", "p2", "");
        await SignalingInterop.Message(id, "signal", "p3", "sdp");
        await SignalingInterop.Message(id, "error", "", "cannot join");

        Assert.Equal("p1", joined);
        Assert.Equal("p2", left);
        Assert.Equal(("p3", "sdp"), signal);
        Assert.Equal("cannot join", error);
    }

    [Fact]
    public async Task AnUnknownMessageType_IsIgnored()
    {
        // The relay may grow a message this client doesn't know; that must not throw across interop.
        var js = new FakeJsRuntime();
        await new Signaling(js).JoinAsync("room", new SignalingHandlers());
        var id = (int)js.ArgsFor("__raskSignal.open")![0]!;

        await SignalingInterop.Message(id, "something-new", "", "");
    }

    [Fact]
    public async Task Closed_FiresOnceAndStopsDelivery()
    {
        var js = new FakeJsRuntime();
        var closed = 0;
        var afterClose = false;

        await new Signaling(js).JoinAsync("room", new SignalingHandlers
        {
            OnClosed = () => { closed++; return Task.CompletedTask; },
            OnPeerJoined = _ => { afterClose = true; return Task.CompletedTask; }
        });
        var id = (int)js.ArgsFor("__raskSignal.open")![0]!;

        await SignalingInterop.Closed(id);
        await SignalingInterop.Closed(id);
        await SignalingInterop.Message(id, "peer-joined", "p", "");

        Assert.Equal(1, closed);
        Assert.False(afterClose);
    }

    [Fact]
    public async Task DisposeAsync_ClosesOnceAndStopsDelivery()
    {
        var js = new FakeJsRuntime();
        var fired = false;
        var connection = await new Signaling(js).JoinAsync("room", new SignalingHandlers
        {
            OnPeerJoined = _ => { fired = true; return Task.CompletedTask; }
        });
        var id = (int)js.ArgsFor("__raskSignal.open")![0]!;

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        Assert.Equal(1, js.CallCount("__raskSignal.close"));

        await SignalingInterop.Message(id, "peer-joined", "p", "");
        Assert.False(fired);
    }

    [Fact]
    public async Task MessagesForAnUnknownConnectionAreIgnored()
    {
        await SignalingInterop.Message(int.MaxValue, "signal", "p", "x");
        await SignalingInterop.Closed(int.MaxValue);
    }

    [Fact]
    public async Task NullAndEmptyArgumentsAreRejected()
    {
        var js = new FakeJsRuntime();
        var signaling = new Signaling(js);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            signaling.JoinAsync("", new SignalingHandlers()).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => signaling.JoinAsync("r", null!).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            signaling.JoinAsync("r", new SignalingHandlers(), "").AsTask());

        var connection = await signaling.JoinAsync("room", new SignalingHandlers());
        await Assert.ThrowsAsync<ArgumentException>(() => connection.SendAsync("", "x").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => connection.SendAsync("p", null!).AsTask());
    }
}
