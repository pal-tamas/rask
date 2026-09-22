using Rask.Core.Browser;

namespace Rask.Wasm.Tests.Browser;

public class NotificationsTests
{
    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskNotify.isSupported", true);

        Assert.True(await new Notifications(js).IsSupportedAsync());
    }

    [Theory]
    [InlineData("granted", NotificationPermission.Granted)]
    [InlineData("denied", NotificationPermission.Denied)]
    [InlineData("default", NotificationPermission.Default)]
    [InlineData(null, NotificationPermission.Default)]
    public async Task The_permission_is_read_from_Notification_permission_as_a_property(string? raw, NotificationPermission expected)
    {
        var js = new FakeJsRuntime();
        if (raw is not null)
        {
            js.SetResponse("Notification.permission", raw);
        }

        Assert.Equal(expected, await new Notifications(js).PermissionAsync());
        Assert.Empty(js.ArgsFor("Notification.permission")!);
    }

    [Theory]
    [InlineData("granted", NotificationPermission.Granted)]
    [InlineData("denied", NotificationPermission.Denied)]
    public async Task Requesting_permission_maps_the_result(string raw, NotificationPermission expected)
    {
        var js = new FakeJsRuntime();
        js.SetResponse("Notification.requestPermission", raw);

        Assert.Equal(expected, await new Notifications(js).RequestPermissionAsync());
    }

    [Fact]
    public async Task Showing_sends_the_title_and_options()
    {
        var js = new FakeJsRuntime();
        var opts = new NotificationOptions { Body = "hi", Tag = "t", RequireInteraction = true };

        await new Notifications(js).ShowAsync("Title", opts);

        Assert.Equal(["Title", opts], js.ArgsFor("__raskNotify.show"));
    }

    [Fact]
    public async Task Showing_with_null_options_sends_default_options()
    {
        var js = new FakeJsRuntime();

        await new Notifications(js).ShowAsync("Title");

        var args = js.ArgsFor("__raskNotify.show");
        Assert.Equal("Title", args![0]);
        Assert.IsType<NotificationOptions>(args[1]);
    }

    [Fact]
    public async Task Showing_a_null_title_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new Notifications(new FakeJsRuntime()).ShowAsync(null!));
    }
}
