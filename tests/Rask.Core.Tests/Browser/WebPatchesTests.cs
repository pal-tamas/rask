using System.Text.Json;

namespace Rask.Core.Tests.Browser;

/// <summary>
///     Rask.Web's browser-quirk patches (<c>rask-web-patches.ts</c>), each driven in a node subprocess against a stub of
///     the browser that has the quirk.
/// </summary>
public class WebPatchesTests
{
    private static JsonElement? Result => NodeFixture.Run("WebPatchesFixture");

    [Fact]
    public void A_screen_wake_lock_is_asked_for_again_when_the_page_is_visible_until_it_is_released()
    {
        // Node is not required to build or test Rask; the browser-observable half is covered by E2E.
        if (Result is not { } r) return;

        Assert.Equal("screen", r.GetProperty("wakeType").GetString());
        Assert.Equal(1, r.GetProperty("wakeRequestsWhileHidden").GetInt32());
        Assert.Equal(2, r.GetProperty("wakeRequestsAfterVisible").GetInt32());
        Assert.False(r.GetProperty("wakeReleasedAfterVisible").GetBoolean());
        Assert.True(r.GetProperty("wakeReleasedAfterRelease").GetBoolean());
        Assert.Equal(1, r.GetProperty("wakeReleaseEvents").GetInt32());
        Assert.Equal(2, r.GetProperty("wakeRequestsAfterRelease").GetInt32());
        Assert.True(r.GetProperty("wakeOtherCallUnpatched").GetBoolean());
    }

    [Fact]
    public void A_wake_lock_the_browser_refuses_back_is_released_once_for_good()
    {
        if (Result is not { } r) return;

        Assert.True(r.GetProperty("wakeRefusedReleased").GetBoolean());
        Assert.Equal(1, r.GetProperty("wakeRefusedReleaseEvents").GetInt32());
    }

    [Fact]
    public void SpeechRecognition_is_found_under_the_webkit_prefix_only_where_the_window_lacks_it()
    {
        if (Result is not { } r) return;

        Assert.Equal("SpeechRecognition", r.GetProperty("speechWhenMissing").GetString());
        Assert.Equal("webkitSpeechRecognition", r.GetProperty("speechWhenPrefixed").GetString());
        Assert.Equal("SpeechRecognition", r.GetProperty("speechWhenUnprefixed").GetString());
        Assert.Equal("SpeechRecognition", r.GetProperty("speechOnAnotherObject").GetString());
    }

    [Fact]
    public void An_install_prompt_fired_before_anyone_listened_is_handed_to_a_later_subscriber_until_it_is_spent()
    {
        if (Result is not { } r) return;

        Assert.Equal(0, r.GetProperty("installReplayedAtOnce").GetInt32());
        Assert.True(r.GetProperty("installReplayedSame").GetBoolean());
        Assert.Equal(0, r.GetProperty("installOtherEventsNotReplayed").GetInt32());
        Assert.Equal(1, r.GetProperty("installPrompted").GetInt32());
        Assert.Equal(0, r.GetProperty("installReplayedWhenSpent").GetInt32());
        Assert.Equal("unavailable", r.GetProperty("installUnavailableWhenSpent").GetString());
    }

    [Fact]
    public void Chromiums_install_prompt_answer_is_MDNs_and_Trigger_Install_shows_the_kept_prompt()
    {
        if (Result is not { } r) return;

        Assert.Equal("accepted", r.GetProperty("installAnswer").GetProperty("userChoice").GetString());
        Assert.Equal("unavailable", r.GetProperty("installUnavailableBeforeBoot").GetString());
        Assert.Equal("accepted", r.GetProperty("installTriggered").GetString());
        Assert.Equal("unavailable", r.GetProperty("installAfterInstalled").GetString());
    }
}
