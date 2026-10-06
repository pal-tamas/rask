namespace Rask.WebPush;

/// <summary>
/// The table already holds <see cref="WebPushOptions.MaxAnonymousSubscribers" /> subscriptions for visitors
/// who are not signed in, so one more was not stored.
/// </summary>
internal sealed class PushSubscriberLimitException(int limit)
    : InvalidOperationException(
        $"The push subscriber table already holds {limit} subscriptions for signed-out visitors "
        + "(Rask:Push:MaxAnonymousSubscribers), so this one was not stored.");
