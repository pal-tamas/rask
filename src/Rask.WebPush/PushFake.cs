using Rask.Batteries;
using Rask.Data;
using Rask.Wire;

namespace Rask.WebPush;

/// <summary>
/// Records what was sent and who subscribed, and answers in sentences: <c>push.Sent().To(userId).Once()</c>,
/// <c>push.Sent().WithTitle("Order shipped").Once()</c>. Every recorded push counts as delivered to one browser.
/// </summary>
public sealed class PushFake : IPush, IDisposable
{
    private readonly List<SentPush> _sent = [];
    private readonly List<PushSubscription> _subscribed = [];
    private readonly IPush? _previous;
    private readonly Lock _gate = new();

    internal PushFake()
    {
        _previous = Push.Faked.Value;
        Push.Faked.Value = this;
    }

    /// <summary>The fake's own public key: a browser asked to subscribe against it gets a stable answer.</summary>
    public string? PublicKey { get; set; } = "fake-public-key";

    /// <summary>The pushes this test sent, ready to be narrowed and counted.</summary>
    public Counting<SentPush> Sent()
    {
        lock (_gate)
        {
            return new Counting<SentPush>(
                [.. _sent], "push", "sent",
                static p => $"\"{p.Title}\"{(p.To is { } to ? $" to {to}" : " to everyone")}");
        }
    }

    /// <summary>The subscriptions this test kept.</summary>
    public Counting<PushSubscription> Subscribed()
    {
        lock (_gate)
        {
            return new Counting<PushSubscription>([.. _subscribed], "subscription", "kept", static s => s.Endpoint);
        }
    }

    /// <summary>Forgets everything recorded so far.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _sent.Clear();
            _subscribed.Clear();
        }
    }

    public void Dispose() => Push.Faked.Value = _previous;

    Task<PushSubscriber> IPush.Subscribe(PushSubscription subscription, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (_gate)
        {
            _subscribed.Add(subscription);
        }

        return Task.FromResult(PushSubscriber.For(subscription, Current.UserId, DateTime.UtcNow));
    }

    Task<bool> IPush.Unsubscribe(string endpoint, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_subscribed.RemoveAll(s => string.Equals(s.Endpoint, endpoint, StringComparison.Ordinal)) > 0);
        }
    }

    Task<WebPushResult> IPush.Send(PushSubscription subscription, WebPushMessage message, CancellationToken cancellationToken)
    {
        Record(message, null);
        return Task.FromResult(new WebPushResult(WebPushStatus.Success, 201));
    }

    Task<int> IPush.Deliver(WebPushMessage message, Guid? userId, CancellationToken cancellationToken)
    {
        Record(message, userId);
        return Task.FromResult(1);
    }

    private void Record(WebPushMessage message, Guid? to)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            _sent.Add(new SentPush(message.Title, message.Body, message.Url, to));
        }
    }
}
