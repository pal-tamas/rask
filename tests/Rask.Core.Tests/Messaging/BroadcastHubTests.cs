using Rask.Core.Live;
using Rask.Core.Messaging;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Messaging;

// #1061: the in-process broadcast hub — who receives a message, when a subscription ends, and that one bad subscriber
// cannot stop the rest.
public sealed partial class BroadcastHubTests
{
    private static readonly Topic<string> News = new("news");

    [Fact]
    public async Task Every_subscriber_in_a_session_is_run_in_one_delivery_and_marked_for_render()
    {
        var hub = new BroadcastHub();
        var session = new QueueHandle();
        var first = Mounted(session);
        var second = Mounted(session);
        var received = new List<string>();
        hub.Subscribe(first, News, m => received.Add("first:" + m));
        hub.Subscribe(second, News, async m =>
        {
            await Task.Yield();
            received.Add("second:" + m);
        });

        await hub.PublishAsync(News, "hello");

        // Queued, not run: delivery happens on the session's own queue.
        Assert.Empty(received);
        var delivery = Assert.Single(session.Queued);

        await delivery();
        Assert.Equal(["first:hello", "second:hello"], received);
        Assert.True(session.RenderRequests >= 1);
    }

    [Fact]
    public async Task Each_session_gets_its_own_delivery()
    {
        var hub = new BroadcastHub();
        var a = new QueueHandle();
        var b = new QueueHandle();
        hub.Subscribe(Mounted(a), News, _ => { });
        hub.Subscribe(Mounted(b), News, _ => { });

        await hub.PublishAsync(News, "x");

        Assert.Single(a.Queued);
        Assert.Single(b.Queued);
    }

    [Fact]
    public async Task A_topic_is_its_name_and_its_type()
    {
        var hub = new BroadcastHub();
        var session = new QueueHandle();
        var strings = 0;
        var numbers = 0;
        hub.Subscribe(Mounted(session), new Topic<string>("news"), _ => strings++);
        hub.Subscribe(Mounted(session), new Topic<int>("news"), _ => numbers++);

        await hub.PublishAsync(News, "same name, same type — another instance");
        foreach (var delivery in session.Queued)
        {
            await delivery();
        }

        Assert.Equal(1, strings);
        Assert.Equal(0, numbers);
    }

    [Fact]
    public void A_subscription_ends_when_its_owner_unmounts()
    {
        var hub = new BroadcastHub();
        var owner = Mounted(new QueueHandle());
        hub.Subscribe(owner, News, _ => { });
        Assert.Equal(1, hub.SubscriberCount(News));

        ComponentLifecycle.DisposeComponentTree(owner);

        Assert.Equal(0, hub.SubscriberCount(News));
    }

    [Fact]
    public void A_component_that_has_already_unmounted_cannot_subscribe()
    {
        var hub = new BroadcastHub();
        var owner = Mounted(new QueueHandle());
        ComponentLifecycle.DisposeComponentTree(owner);

        hub.Subscribe(owner, News, _ => { });

        Assert.Equal(0, hub.SubscriberCount(News));
    }

    [Fact]
    public async Task A_subscriber_that_unmounts_after_the_publish_is_skipped()
    {
        var hub = new BroadcastHub();
        var session = new QueueHandle();
        var leaving = Mounted(session);
        var ran = false;
        hub.Subscribe(leaving, News, _ => ran = true);

        await hub.PublishAsync(News, "x");
        ComponentLifecycle.DisposeComponentTree(leaving);
        await session.Queued.Single()();

        Assert.False(ran);
    }

    [Fact]
    public async Task One_subscriber_throwing_does_not_stop_the_others()
    {
        var hub = new BroadcastHub();
        var session = new QueueHandle();
        var reached = false;
        hub.Subscribe(Mounted(session), News, (string _) => throw new InvalidOperationException("boom"));
        hub.Subscribe(Mounted(session), News, _ => reached = true);

        await hub.PublishAsync(News, "x");
        await session.Queued.Single()();

        Assert.True(reached);
    }

    [Fact]
    public async Task Publishing_to_a_topic_nobody_subscribes_to_is_a_no_op() =>
        await new BroadcastHub().PublishAsync(new Topic<Guid>("nobody"), Guid.NewGuid());

    [Fact]
    public void A_topic_needs_a_name() =>
        Assert.ThrowsAny<ArgumentException>(() => new Topic<string>(" "));

    private static Probe Mounted(IRenderHandle handle)
    {
        var probe = new Probe { RenderHandle = handle };
        probe.RaiseLifecycleBeforeRender(true);
        return probe;
    }

    private sealed class Probe : Component
    {
        protected override Component? Render() => Span;
    }

    // A session with a queue the test drains by hand, so "queued" and "run" are observably different.
    private sealed class QueueHandle : IRenderHandle
    {
        public List<Func<Task>> Queued { get; } = [];

        public int RenderRequests { get; private set; }

        public Task RequestRenderAsync()
        {
            RenderRequests++;
            return Task.CompletedTask;
        }

        Task IRenderHandle.DeliverAsync(Func<Task> work)
        {
            Queued.Add(work);
            return Task.CompletedTask;
        }
    }
}
