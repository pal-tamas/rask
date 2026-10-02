using Rask.Web.Types;
using Rask.WebPush;

namespace Rask.Server.Tests.App;

// A component on the server host hands Push.Subscribe what MDN's PushSubscription.toJSON() answered: the keys nested.
public sealed class BrowserPushSubscriptionsTests
{
    [Fact]
    public async Task A_subscription_as_MDN_serializes_it_is_kept_with_its_keys_flattened()
    {
        using var push = Push.Fake();
        var json = new PushSubscriptionJSON
        {
            Endpoint = "https://push.example/phone",
            ExpirationTime = 1_700_000_000_000,
            Keys = new() { ["p256dh"] = "BKey", ["auth"] = "secret" },
        };

        await Push.Subscribe(json, TestContext.Current.CancellationToken);

        var kept = push.Subscribed().Only();
        Assert.Equal(new Wire.PushSubscription("https://push.example/phone", "BKey", "secret", 1_700_000_000_000), kept);
    }

    [Fact]
    public async Task A_subscription_missing_its_keys_reaches_the_battery_with_them_empty()
    {
        using var push = Push.Fake();

        await Push.Subscribe(new PushSubscriptionJSON { Endpoint = "https://push.example/phone" }, TestContext.Current.CancellationToken);

        var kept = push.Subscribed().Only();
        Assert.Equal(("", ""), (kept.P256dh, kept.Auth));
    }
}
