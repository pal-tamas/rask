using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     When <c>&lt;html&gt;</c> says <c>data-rask-offline</c> (Livewire's <c>wire:offline</c>): while the browser reports
///     itself offline, and while a host's live connection is down — whichever of the two is true.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-offline.ts</c> in a Node subprocess (<c>OfflineFixture.ts</c>). The journeys on
///     a real socket are <c>OfflineAndReconnectTests</c> in <c>Rask.Server.E2E.Tests</c>.
/// </remarks>
public sealed class OfflineMarkClientTests
{
    private static JsonElement? Run() => NodeFixture.Run("OfflineFixture");

    [Fact]
    public void An_online_page_carries_no_mark_and_the_browser_going_offline_adds_it_and_coming_back_takes_it_off()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var online = root.GetProperty("online").GetBoolean();
        var offline = root.GetProperty("afterTheBrowserWentOffline").GetBoolean();
        var back = root.GetProperty("afterItCameBack").GetBoolean();

        Assert.False(online);
        Assert.True(offline);
        Assert.False(back);
    }

    [Fact]
    public void A_connection_that_is_down_marks_the_page_though_the_browser_is_online_and_only_its_return_clears_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var down = root.GetProperty("whileTheConnectionIsDown").GetBoolean();
        var browserBack = root.GetProperty("browserBackButSocketStillDown").GetBoolean();
        var reopened = root.GetProperty("afterTheSocketReopened").GetBoolean();

        Assert.True(down);
        Assert.True(browserBack);
        Assert.False(reopened);
    }
}
