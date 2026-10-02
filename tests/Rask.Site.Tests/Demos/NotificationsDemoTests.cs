using Rask.Site.Tests.Infrastructure;
using Rask.Web;

namespace Rask.Site.Tests.Demos;

/// <summary>
///     The Notifications + Badge showcase, with MDN's <c>Notification</c> and <c>navigator</c> faked so no browser is
///     needed: it mounts its buttons idle, and says what it asked the browser for.
/// </summary>
public sealed partial class NotificationsDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Rendering_mounts_the_permission_notify_and_badge_buttons_idle()
    {
        var page = Page.Render(() => NotificationsDemo, TestServices.Default());

        var html = page.Render();

        Assert.Contains("id=\"notif-permission\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"notif-show\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"badge-set\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"badge-clear\"", html, StringComparison.Ordinal);
        Assert.Contains("(idle)", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setting_the_badge_asks_the_navigator_for_three()
    {
        using var navigator = Navigator.Fake();
        var page = Page.Render(() => NotificationsDemo, TestServices.Default());
        var setBadge = MarkupAssert.Attrs(page.Render(), "data-rask-on-click")[2];   // Permission, Notify, Set, Clear

        await page.Invoke(setBadge, "{}");

        Assert.Contains("Badge set to 3", page.Render(), StringComparison.Ordinal);
        Assert.Equal("setAppBadge", navigator.Calls.Single().Member);
    }
}
