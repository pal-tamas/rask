using Rask.Core.Browser;
using Rask.Core.Tests.Interop;

namespace Rask.Core.Tests.Browser;

public class WebRtcTests
{
    [Fact]
    public async Task IsSupportedAsync_CallsTheHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.isSupported", true);

        Assert.True(await new WebRtc(js).IsSupportedAsync());
    }

    [Fact]
    public async Task CreateAsync_PassesTheConfigAndMintsAConnectionId()
    {
        var js = new FakeJsRuntime();
        var config = new RtcConfiguration { IceServers = ["stun:stun.example.com:3478"] };

        await new WebRtc(js).CreateAsync(config, new RtcHandlers());

        var args = js.ArgsFor("__raskRtc.create")!;
        Assert.IsType<int>(args[0]);
        Assert.Same(config, args[1]);
    }

    [Fact]
    public async Task CreateAsync_MintsADistinctIdPerConnection()
    {
        var js = new FakeJsRuntime();
        var rtc = new WebRtc(js);

        await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers());
        await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers());

        var ids = js.Calls.Where(c => c.Identifier == "__raskRtc.create").Select(c => c.Args![0]).ToArray();
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Theory]
    [InlineData("http://evil.example.com")]
    [InlineData("ws://evil.example.com")]
    [InlineData("stunn:stun.example.com")]
    public async Task CreateAsync_RejectsAnIceServerThatIsNotStunOrTurn(string url)
    {
        var js = new FakeJsRuntime();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            new WebRtc(js).CreateAsync(new RtcConfiguration { IceServers = [url] }, new RtcHandlers()).AsTask());

        Assert.Contains(url, ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, js.CallCount("__raskRtc.create"));
    }

    [Theory]
    [InlineData("stun:stun.example.com:3478")]
    [InlineData("turn:turn.example.com:3478")]
    [InlineData("turns:turn.example.com:5349")]
    [InlineData("STUN:stun.example.com:3478")]
    public async Task CreateAsync_AcceptsEveryIceScheme(string url)
    {
        var js = new FakeJsRuntime();

        await new WebRtc(js).CreateAsync(new RtcConfiguration { IceServers = [url] }, new RtcHandlers());

        Assert.Equal(1, js.CallCount("__raskRtc.create"));
    }

    [Fact]
    public async Task CreateAsync_RejectsAnUnknownTransportPolicy()
    {
        var js = new FakeJsRuntime();

        await Assert.ThrowsAsync<ArgumentException>(() => new WebRtc(js)
            .CreateAsync(new RtcConfiguration { IceTransportPolicy = "none" }, new RtcHandlers()).AsTask());
    }

    [Fact]
    public async Task CreateAsync_UnregistersTheConnectionWhenTheJsCallThrows()
    {
        var js = new FakeJsRuntime();
        js.SetException("__raskRtc.create", new InvalidOperationException("boom"));
        var fired = false;

        var handlers = new RtcHandlers
        {
            OnConnectionStateChanged = _ =>
            {
                fired = true;
                return Task.CompletedTask;
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WebRtc(js).CreateAsync(new RtcConfiguration(), handlers).AsTask());

        // Ids start at 1 and this is the only connection this test creates, but the counter is static and
        // shared, so probe every id that could plausibly be ours rather than guessing one.
        for (var id = 1; id <= 64; id++)
        {
            await WebRtcInterop.State(id, "connected");
        }

        Assert.False(fired);
    }

    [Fact]
    public async Task Offer_Answer_AndDescriptions_RoundTripThroughTheConnectionId()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.createOffer", new RtcDescription("offer", "v=0"));
        var conn = await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers());
        var id = js.ArgsFor("__raskRtc.create")![0];

        var offer = await conn.CreateOfferAsync();
        await conn.SetLocalDescriptionAsync(offer);
        await conn.SetRemoteDescriptionAsync(new RtcDescription("answer", "v=1"));
        await conn.AddIceCandidateAsync(new RtcIceCandidate("candidate:1", "0", 0));

        Assert.Equal("v=0", offer.Sdp);
        Assert.Equal([id], js.ArgsFor("__raskRtc.createOffer"));
        Assert.Equal(id, js.ArgsFor("__raskRtc.setLocal")![0]);
        Assert.Equal(id, js.ArgsFor("__raskRtc.setRemote")![0]);
        Assert.Equal(id, js.ArgsFor("__raskRtc.addIce")![0]);
    }

    [Fact]
    public async Task CreateDataChannelAsync_ReturnsAHandleOverTheJsMintedId()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.createChannel", 7);
        var conn = await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers());

        var channel = await conn.CreateDataChannelAsync("chat");

        Assert.Equal("chat", channel.Label);
        Assert.Equal("chat", js.ArgsFor("__raskRtc.createChannel")![1]);

        await channel.SendAsync("hi");
        Assert.Equal([7, "hi"], js.ArgsFor("__raskRtc.sendText"));
    }

    [Fact]
    public async Task SendAsync_BytesRideBase64Encoded()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.createChannel", 3);
        var conn = await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers());
        var channel = await conn.CreateDataChannelAsync("bin");

        await channel.SendAsync([1, 2, 3]);

        Assert.Equal([3, Convert.ToBase64String([1, 2, 3])], js.ArgsFor("__raskRtc.sendBytes"));
    }

    [Fact]
    public async Task CreateDataChannelAsync_RejectsAnEmptyLabel()
    {
        var js = new FakeJsRuntime();
        var conn = await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers());

        await Assert.ThrowsAsync<ArgumentException>(() => conn.CreateDataChannelAsync("").AsTask());
    }

    [Fact]
    public async Task ListenAsync_DeliversABatchAndDecodesBothMessageShapes()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.createChannel", 11);
        var conn = await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers());
        var connectionId = (int)js.ArgsFor("__raskRtc.create")![0]!;
        var channel = await conn.CreateDataChannelAsync("chat");

        var batches = new List<IReadOnlyList<RtcMessage>>();
        await channel.ListenAsync(b =>
        {
            batches.Add(b);
            return Task.CompletedTask;
        });

        await WebRtcInterop.Messages(
            connectionId, 11,
            [new RtcMessageWire("hello", null), new RtcMessageWire(null, Convert.ToBase64String([9, 8]))],
            0);

        // One push, one callback — the batch is the unit of delivery, not the message.
        var batch = Assert.Single(batches);
        Assert.Equal(2, batch.Count);
        Assert.Equal("hello", batch[0].Text);
        Assert.Null(batch[0].Data);
        Assert.Equal([9, 8], batch[1].Data);
        Assert.Null(batch[1].Text);
        Assert.Equal([11], js.ArgsFor("__raskRtc.listen"));
    }

    [Fact]
    public async Task Messages_ForTheSameChannelIdOnAnotherConnection_DoNotCross()
    {
        // The Server host runs many sessions in one process and JS mints channel ids per client, so two
        // sessions both see channel #5. Only the (connection, channel) pair keeps them apart.
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.createChannel", 5);
        var rtc = new WebRtc(js);

        var first = await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers());
        var firstId = (int)js.Calls.Last(c => c.Identifier == "__raskRtc.create").Args![0]!;
        var second = await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers());

        var firstReceived = 0;
        var secondReceived = 0;
        var firstChannel = await first.CreateDataChannelAsync("chat");
        var secondChannel = await second.CreateDataChannelAsync("chat");
        await firstChannel.ListenAsync(b =>
        {
            firstReceived += b.Count;
            return Task.CompletedTask;
        });
        // Registered second, and under the same JS-minted channel id. Keyed by channel alone, this
        // registration would replace the first one and the message below would go to the wrong session.
        await secondChannel.ListenAsync(b =>
        {
            secondReceived += b.Count;
            return Task.CompletedTask;
        });

        await WebRtcInterop.Messages(firstId, 5, [new RtcMessageWire("for-first", null)], 0);

        Assert.Equal(1, firstReceived);
        Assert.Equal(0, secondReceived);
    }

    [Fact]
    public async Task Ice_DeliversTheWholeBatchToTheConnectionsHandler()
    {
        var js = new FakeJsRuntime();
        var received = new List<RtcIceCandidate>();
        var rtc = new WebRtc(js);

        await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers
        {
            OnIceCandidates = c =>
            {
                received.AddRange(c);
                return Task.CompletedTask;
            }
        });
        var id = (int)js.ArgsFor("__raskRtc.create")![0]!;

        await WebRtcInterop.Ice(id, [new RtcIceCandidate("a", "0", 0), new RtcIceCandidate("b", "0", 0)]);

        Assert.Equal(["a", "b"], received.Select(c => c.Candidate));
    }

    [Theory]
    [InlineData("connecting", RtcConnectionState.Connecting)]
    [InlineData("connected", RtcConnectionState.Connected)]
    [InlineData("disconnected", RtcConnectionState.Disconnected)]
    [InlineData("failed", RtcConnectionState.Failed)]
    [InlineData("closed", RtcConnectionState.Closed)]
    [InlineData("new", RtcConnectionState.New)]
    [InlineData("something-the-spec-adds-later", RtcConnectionState.New)]
    public async Task State_MapsEveryBrowserStateName(string name, RtcConnectionState expected)
    {
        var js = new FakeJsRuntime();
        RtcConnectionState? seen = null;

        await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers
        {
            OnConnectionStateChanged = s =>
            {
                seen = s;
                return Task.CompletedTask;
            }
        });
        var id = (int)js.ArgsFor("__raskRtc.create")![0]!;

        await WebRtcInterop.State(id, name);

        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task Channel_HandsTheAppAUsableHandleForARemoteOpenedChannel()
    {
        var js = new FakeJsRuntime();
        IRtcDataChannel? adopted = null;

        await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers
        {
            OnDataChannel = ch =>
            {
                adopted = ch;
                return Task.CompletedTask;
            }
        });
        var id = (int)js.ArgsFor("__raskRtc.create")![0]!;

        await WebRtcInterop.Channel(id, 42, "from-peer");

        Assert.NotNull(adopted);
        Assert.Equal("from-peer", adopted.Label);

        await adopted.SendAsync("pong");
        Assert.Equal([42, "pong"], js.ArgsFor("__raskRtc.sendText"));
    }

    [Fact]
    public async Task Channel_HandsTheSameHandleBackOnARepeatedPush()
    {
        var js = new FakeJsRuntime();
        var handles = new List<IRtcDataChannel>();

        await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers
        {
            OnDataChannel = ch =>
            {
                handles.Add(ch);
                return Task.CompletedTask;
            }
        });
        var id = (int)js.ArgsFor("__raskRtc.create")![0]!;

        await WebRtcInterop.Channel(id, 43, "from-peer");
        await WebRtcInterop.Channel(id, 43, "from-peer");

        Assert.Equal(2, handles.Count);
        Assert.Same(handles[0], handles[1]);
    }

    [Fact]
    public async Task DisposeAsync_ClosesTheConnectionOnceAndStopsDelivery()
    {
        var js = new FakeJsRuntime();
        var fired = false;
        var conn = await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers
        {
            OnConnectionStateChanged = _ =>
            {
                fired = true;
                return Task.CompletedTask;
            }
        });
        var id = (int)js.ArgsFor("__raskRtc.create")![0]!;

        await conn.DisposeAsync();
        await conn.DisposeAsync();

        Assert.Equal(1, js.CallCount("__raskRtc.close"));

        await WebRtcInterop.State(id, "connected");
        Assert.False(fired);
    }

    [Fact]
    public async Task DisposingTheConnection_AlsoStopsItsChannels()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.createChannel", 21);
        var conn = await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers());
        var id = (int)js.ArgsFor("__raskRtc.create")![0]!;
        var channel = await conn.CreateDataChannelAsync("chat");

        var received = 0;
        await channel.ListenAsync(b =>
        {
            received += b.Count;
            return Task.CompletedTask;
        });

        await conn.DisposeAsync();
        await WebRtcInterop.Messages(id, 21, [new RtcMessageWire("late", null)], 0);

        Assert.Equal(0, received);
    }

    [Fact]
    public async Task DisposingAChannel_ClosesItOnceAndStopsDelivery()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.createChannel", 31);
        var conn = await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers());
        var id = (int)js.ArgsFor("__raskRtc.create")![0]!;
        var channel = await conn.CreateDataChannelAsync("chat");

        var received = 0;
        await channel.ListenAsync(b =>
        {
            received += b.Count;
            return Task.CompletedTask;
        });

        await channel.DisposeAsync();
        await channel.DisposeAsync();

        Assert.Equal(1, js.CallCount("__raskRtc.closeChannel"));

        await WebRtcInterop.Messages(id, 31, [new RtcMessageWire("late", null)], 0);
        Assert.Equal(0, received);
    }

    [Fact]
    public async Task ChannelClosed_FromTheBrowser_StopsDelivery()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.createChannel", 41);
        var conn = await new WebRtc(js).CreateAsync(new RtcConfiguration(), new RtcHandlers());
        var id = (int)js.ArgsFor("__raskRtc.create")![0]!;
        var channel = await conn.CreateDataChannelAsync("chat");

        var received = 0;
        await channel.ListenAsync(b =>
        {
            received += b.Count;
            return Task.CompletedTask;
        });

        await WebRtcInterop.ChannelClosed(id, 41);
        await WebRtcInterop.Messages(id, 41, [new RtcMessageWire("late", null)], 0);

        Assert.Equal(0, received);
    }

    [Fact]
    public async Task Pushes_ForAnUnknownIdAreIgnored()
    {
        await WebRtcInterop.Ice(int.MaxValue, [new RtcIceCandidate("a", null, null)]);
        await WebRtcInterop.State(int.MaxValue, "connected");
        await WebRtcInterop.Channel(int.MaxValue, 1, "x");
        await WebRtcInterop.Messages(int.MaxValue, 1, [new RtcMessageWire("x", null)], 0);
        await WebRtcInterop.ChannelClosed(int.MaxValue, 1);
    }

    [Fact]
    public async Task NullArgumentsAreRejected()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskRtc.createChannel", 51);
        var rtc = new WebRtc(js);

        await Assert.ThrowsAsync<ArgumentNullException>(() => rtc.CreateAsync(null!, new RtcHandlers()).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            rtc.CreateAsync(new RtcConfiguration(), null!).AsTask());

        var conn = await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers());
        await Assert.ThrowsAsync<ArgumentNullException>(() => conn.SetLocalDescriptionAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => conn.SetRemoteDescriptionAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => conn.AddIceCandidateAsync(null!).AsTask());

        var channel = await conn.CreateDataChannelAsync("chat");
        await Assert.ThrowsAsync<ArgumentNullException>(() => channel.ListenAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => channel.SendAsync((string)null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => channel.SendAsync((byte[])null!).AsTask());
    }
}
