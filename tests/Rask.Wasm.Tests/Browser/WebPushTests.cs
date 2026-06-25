using Rask.Core.Live;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class WebPushTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
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
    public async Task RequestPermission_MapsResult(string? raw, NotificationPermission expected)
    {
        var js = new FakeJsRuntime();
        if (raw is not null)
        {
            js.SetResponse("__raskPush.requestPermission", raw);
        }

        Assert.Equal(expected, await new WebPush(js).RequestPermissionAsync());
    }

    [Fact]
    public async Task RegisterServiceWorker_DefaultsToFrameworkSw_UnderPathBase()
    {
        var js = new FakeJsRuntime();

        await new WebPush(js).RegisterServiceWorkerAsync();

        Assert.Equal([$"{LiveOptions.PathBase}/rask-sw.js"], js.ArgsFor("__raskPush.register"));
    }

    [Fact]
    public async Task RegisterServiceWorker_UsesProvidedUrl()
    {
        var js = new FakeJsRuntime();

        await new WebPush(js).RegisterServiceWorkerAsync("/custom-sw.js");

        Assert.Equal(["/custom-sw.js"], js.ArgsFor("__raskPush.register"));
    }

    [Fact]
    public async Task Subscribe_SendsVapidKey_AndReturnsSubscription()
    {
        var js = new FakeJsRuntime();
        var expected = new PushSubscription("https://push.example/abc", "p256", "auth", null);
        js.SetResponse("__raskPush.subscribe", expected);

        var sub = await new WebPush(js).SubscribeAsync("VAPID_KEY");

        Assert.Equal(expected, sub);
        Assert.Equal(["VAPID_KEY"], js.ArgsFor("__raskPush.subscribe"));
    }

    [Fact]
    public async Task Subscribe_EmptyKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await new WebPush(new FakeJsRuntime()).SubscribeAsync(""));
    }

    [Fact]
    public async Task GetSubscription_And_Unsubscribe_UseHelpers()
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
