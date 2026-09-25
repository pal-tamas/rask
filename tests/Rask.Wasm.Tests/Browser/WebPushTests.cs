using Rask.Core.Browser;
using Rask.Core.Live;
using Rask.Wire;

namespace Rask.Wasm.Tests.Browser;

public class WebPushTests
{
    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskPush.isSupported", true);

        Assert.True(await new WebPush(js).IsSupportedAsync());
    }

    [Theory]
    [InlineData("granted", NotificationPermission.Granted)]
    [InlineData("denied", NotificationPermission.Denied)]
    [InlineData("default", NotificationPermission.Default)]
    [InlineData(null, NotificationPermission.Default)]
    public async Task Requesting_permission_maps_the_result(string? raw, NotificationPermission expected)
    {
        var js = new FakeJsRuntime();
        if (raw is not null)
        {
            js.SetResponse("__raskPush.requestPermission", raw);
        }

        Assert.Equal(expected, await new WebPush(js).RequestPermissionAsync());
    }

    [Fact]
    public async Task Registering_the_service_worker_defaults_to_the_frameworks_worker_under_the_path_base()
    {
        var js = new FakeJsRuntime();

        await new WebPush(js).RegisterServiceWorkerAsync();

        Assert.Equal([$"{LiveOptions.PathBase}/rask-sw.js"], js.ArgsFor("__raskPush.register"));
    }

    [Fact]
    public async Task Registering_the_service_worker_uses_the_provided_url()
    {
        var js = new FakeJsRuntime();

        await new WebPush(js).RegisterServiceWorkerAsync("/custom-sw.js");

        Assert.Equal(["/custom-sw.js"], js.ArgsFor("__raskPush.register"));
    }

    [Fact]
    public async Task Subscribing_sends_the_VAPID_key_and_returns_the_subscription()
    {
        var js = new FakeJsRuntime();
        var expected = new PushSubscription("https://push.example/abc", "p256", "auth", null);
        js.SetResponse("__raskPush.subscribe", expected);

        var sub = await new WebPush(js).SubscribeAsync("VAPID_KEY");

        Assert.Equal(expected, sub);
        Assert.Equal(["VAPID_KEY"], js.ArgsFor("__raskPush.subscribe"));
    }

    [Fact]
    public async Task Subscribing_with_an_empty_key_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await new WebPush(new FakeJsRuntime()).SubscribeAsync(""));
    }

    [Fact]
    public async Task Getting_the_subscription_and_unsubscribing_use_the_helpers()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskPush.unsubscribe", true);
        var push = new WebPush(js);

        await push.GetSubscriptionAsync();
        var removed = await push.UnsubscribeAsync();

        Assert.Equal(1, js.CallCount("__raskPush.getSubscription"));
        Assert.True(removed);
    }
}
