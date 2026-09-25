using Rask.Batteries;
using Rask.Data;
using Rask.Wire;

namespace Rask.WebPush;

/// <summary><c>using var push = Push.Fake();</c> — this test's pushes go nowhere and can be asked about.</summary>
public static class PushFakes
{
    extension(Push)
    {
        /// <summary>Stands in for the battery for this test's flow alone; parallel tests never see each other's pushes.</summary>
        public static PushFake Fake() => new();
    }
}

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
            return Task.FromResult(_subscribed.RemoveAll(s => s.Endpoint == endpoint) > 0);
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

/// <summary>One push a <see cref="PushFake" /> recorded.</summary>
/// <param name="Title">The notification's title.</param>
/// <param name="Body">Its body text.</param>
/// <param name="Url">Where a click opens.</param>
/// <param name="To">The user it was addressed to, or <see langword="null" /> for everyone.</param>
public sealed record SentPush(string? Title, string? Body, string? Url, Guid? To);

/// <summary>The steps that narrow <c>push.Sent()</c>.</summary>
public static class SentPushCounting
{
    extension(Counting<SentPush> sent)
    {
        /// <summary>Only the pushes addressed to this user.</summary>
        public Counting<SentPush> To(Guid userId) => sent.Where(p => p.To == userId, $"to {userId}");

        /// <summary>Only the pushes sent to everyone.</summary>
        public Counting<SentPush> ToEveryone() => sent.Where(p => p.To is null, "to everyone");

        /// <summary>Only the pushes with this title.</summary>
        public Counting<SentPush> WithTitle(string title) =>
            sent.Where(p => string.Equals(p.Title, title, StringComparison.Ordinal), $"titled \"{title}\"");

        /// <summary>Only the pushes whose body contains this text.</summary>
        public Counting<SentPush> Saying(string text) =>
            sent.Where(p => p.Body?.Contains(text, StringComparison.Ordinal) == true, $"saying \"{text}\"");
    }
}
