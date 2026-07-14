using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Browser;
using Rask.Example.Shared.Features;

#pragma warning disable RASK014 // test renders the demo component directly as a root

namespace Rask.Example.Shared.Tests.Demos;

// The Notifications + Badge showcase: it injects INotifications/IBadge and renders a button row that drives
// them. Assert the demo mounts its live buttons (the OS notification/badge behaviour is device-specific and
// covered by the native backends, not here) so a regression in the wiring is caught without an E2E.
public sealed class NotificationsDemoTests
{
    [Fact]
    public void Render_MountsPermissionNotifyAndBadgeButtons_Idle()
    {
        var html = Render();

        Assert.Contains("id=\"notif-permission\"", html);
        Assert.Contains("id=\"notif-show\"", html);
        Assert.Contains("id=\"badge-set\"", html);
        Assert.Contains("id=\"badge-clear\"", html);
        // Starts idle until a button runs.
        Assert.Contains("(idle)", html);
    }

    private static string Render()
    {
        INotifications notifications = new FakeNotifications();
        IBadge badge = new FakeBadge();
        var sp = new ServiceCollection()
            .AddSingleton(notifications)
            .AddSingleton(badge)
            .BuildServiceProvider();
        return new NotificationsDemo(notifications, badge).RenderAsLiveRoot(sp);
    }

    private sealed class FakeNotifications : INotifications
    {
        public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

        public ValueTask<NotificationPermission> PermissionAsync() =>
            ValueTask.FromResult(NotificationPermission.Default);

        public ValueTask<NotificationPermission> RequestPermissionAsync() =>
            ValueTask.FromResult(NotificationPermission.Granted);

        public ValueTask ShowAsync(string title, NotificationOptions? options = null) => default;
    }

    private sealed class FakeBadge : IBadge
    {
        public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

        public ValueTask SetAsync(int? count = null) => default;

        public ValueTask ClearAsync() => default;
    }
}
