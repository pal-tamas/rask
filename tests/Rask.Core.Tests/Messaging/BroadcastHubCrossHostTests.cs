using System.Text;
using System.Text.Json.Serialization;
using Rask.Core.Messaging;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Messaging;

// #1115: topics declared with a JsonTypeInfo cross to the app's other hosts through the backplane; every other topic
// stays in its process.
public sealed partial class BroadcastHubTests
{
    private static readonly Topic<Order> Orders = new("orders", CrossHostJson.Default.Order);

    [Fact]
    public async Task A_cross_host_topic_reaches_the_subscribers_on_another_host()
    {
        var bus = new Bus();
        var publisher = new BroadcastHub(bus.Join());
        var other = new BroadcastHub(bus.Join());
        var session = new QueueHandle();
        var received = new List<Order>();
        other.Subscribe(Mounted(session), Orders, received.Add);

        await publisher.PublishAsync(Orders, new Order(7, "Ada"));
        await session.Queued.Single()();

        Assert.Equal([new Order(7, "Ada")], received);
        Assert.Equal("""{"Id":7,"Customer":"Ada"}""", Encoding.UTF8.GetString(Assert.Single(bus.Sent).Payload));
    }

    [Fact]
    public async Task The_publishing_host_delivers_its_own_message_once()
    {
        var bus = new Bus();
        var hub = new BroadcastHub(bus.Join());
        var session = new QueueHandle();
        var received = 0;
        hub.Subscribe(Mounted(session), Orders, _ => received++);

        await hub.PublishAsync(Orders, new Order(1, "Ada"));
        foreach (var delivery in session.Queued)
        {
            await delivery();
        }

        Assert.Equal(1, received);
    }

    [Fact]
    public async Task A_local_topic_never_leaves_the_host()
    {
        var bus = new Bus();
        var hub = new BroadcastHub(bus.Join());
        hub.Subscribe(Mounted(new QueueHandle()), News, _ => { });

        await hub.PublishAsync(News, "only here");

        Assert.Empty(bus.Sent);
        Assert.Empty(bus.Listened);
    }

    [Fact]
    public void A_host_listens_to_a_topic_once_however_many_components_subscribe()
    {
        var bus = new Bus();
        var hub = new BroadcastHub(bus.Join());
        var session = new QueueHandle();

        hub.Subscribe(Mounted(session), Orders, _ => { });
        hub.Subscribe(Mounted(session), Orders, _ => { });
        hub.Subscribe(Mounted(session), new Topic<Order>("orders", CrossHostJson.Default.Order), _ => { });

        Assert.Equal(["orders"], bus.Listened);
    }

    [Fact]
    public async Task One_cross_host_name_cannot_carry_two_types()
    {
        var hub = new BroadcastHub(new Bus().Join());
        hub.Subscribe(Mounted(new QueueHandle()), Orders, _ => { });
        var clash = new Topic<string>("orders", CrossHostJson.Default.String);

        Assert.Throws<InvalidOperationException>(() => hub.Subscribe(Mounted(new QueueHandle()), clash, _ => { }));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await hub.PublishAsync(clash, "x"));
    }

    [Fact]
    public void A_message_another_host_wrote_in_a_shape_this_one_cannot_read_is_dropped()
    {
        var bus = new Bus();
        var hub = new BroadcastHub(bus.Join());
        var session = new QueueHandle();
        hub.Subscribe(Mounted(session), Orders, _ => { });

        bus.Inject("orders", "{\"Id\":\"not a number\"}"u8.ToArray());

        Assert.Empty(session.Queued);
    }

    [Fact]
    public async Task Without_a_backplane_a_cross_host_topic_is_a_local_one()
    {
        var hub = new BroadcastHub();
        var session = new QueueHandle();
        var received = 0;
        hub.Subscribe(Mounted(session), Orders, _ => received++);

        await hub.PublishAsync(Orders, new Order(1, "Ada"));
        await session.Queued.Single()();

        Assert.Equal(1, received);
    }

    [Fact]
    public void A_cross_host_topic_needs_its_contract() =>
        Assert.Throws<ArgumentNullException>(() => new Topic<Order>("orders", null!));

    public sealed record Order(int Id, string Customer);

    // Every host on one in-memory bus. Like a real backplane, it never hands a host its own publish back.
    private sealed class Bus
    {
        private readonly List<(Member Host, string Topic, Action<ReadOnlyMemory<byte>> Received)> _listeners = [];

        public List<(string Topic, byte[] Payload)> Sent { get; } = [];

        public List<string> Listened { get; } = [];

        public Member Join() => new(this);

        public void Inject(string topic, byte[] payload)
        {
            foreach (var listener in _listeners.Where(l => l.Topic == topic))
            {
                listener.Received(payload);
            }
        }

        public sealed class Member(Bus bus) : IBroadcastBackplane
        {
            public Task PublishAsync(string topic, byte[] payload, CancellationToken cancellationToken)
            {
                bus.Sent.Add((topic, payload));
                foreach (var listener in bus._listeners.Where(l => l.Topic == topic && l.Host != this))
                {
                    listener.Received(payload);
                }

                return Task.CompletedTask;
            }

            public void Subscribe(string topic, Action<ReadOnlyMemory<byte>> received)
            {
                bus.Listened.Add(topic);
                bus._listeners.Add((this, topic, received));
            }
        }
    }
}

[JsonSerializable(typeof(BroadcastHubTests.Order))]
[JsonSerializable(typeof(string))]
internal sealed partial class CrossHostJson : JsonSerializerContext;
