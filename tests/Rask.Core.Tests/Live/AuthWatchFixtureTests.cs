using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     When a tab of the Server runtime asks whether the browser's credential still names its user, and what becomes
///     of the frames the page sends meanwhile — driven in a Node subprocess (<c>AuthWatchFixture.ts</c>).
/// </summary>
/// <remarks>
///     A socket sees the browser's cookie once, at its upgrade. The rule these pin is the one a request per update
///     gets for free: once the tab knows the credential changed — or should have asked — nothing leaves on the
///     connection that was opened with the old one, and nothing the reader did is lost.
/// </remarks>
public sealed class AuthWatchFixtureTests
{
    private static JsonElement? Run() => NodeFixture.Run("AuthWatchFixture");

    [Fact]
    public void A_tab_that_has_just_loaded_asks_nothing_and_sends_at_once()
    {
        // No node on PATH — the fixture cannot run. Not a failure: the Server E2E suite drives the same in a browser.
        if (Run() is not { } result)
        {
            return;
        }

        var fresh = result.GetProperty("fresh");

        Assert.Equal(0, fresh.GetProperty("asks").GetInt32());
        Assert.Equal(["1:click"], Strings(fresh.GetProperty("wire")));
    }

    [Fact]
    public void A_tab_come_back_to_asks_at_once_and_holds_what_the_reader_does_until_it_is_answered()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var back = result.GetProperty("cameBackSame");

        Assert.Equal(1, back.GetProperty("askedOnReturn").GetInt32());
        Assert.Empty(Strings(back.GetProperty("wireWhileAsking")));
        Assert.Equal(["1:click"], Strings(back.GetProperty("wire")));
        Assert.Equal(1, back.GetProperty("asks").GetInt32());
        Assert.Equal(0, back.GetProperty("reopens").GetInt32());
    }

    [Fact]
    public void A_changed_credential_opens_the_connection_again_before_anything_is_sent_and_loses_nothing()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var changed = result.GetProperty("cameBackChanged");

        Assert.Empty(Strings(changed.GetProperty("wireBeforeReconnect")));
        Assert.Equal(["2:type", "2:click", "2:after"], Strings(changed.GetProperty("wire")));
        Assert.Equal(1, changed.GetProperty("reopens").GetInt32());
        Assert.Equal(1, changed.GetProperty("told").GetInt32());
        Assert.Equal(1, changed.GetProperty("asks").GetInt32());
    }

    [Fact]
    public void A_hidden_tab_asks_only_when_something_is_about_to_be_sent()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var hidden = result.GetProperty("hidden");

        Assert.Equal(0, hidden.GetProperty("asksWhileHidden").GetInt32());
        Assert.Equal(1, hidden.GetProperty("asksOnFrame").GetInt32());
        Assert.Equal(["1:tick"], Strings(hidden.GetProperty("wire")));
    }

    [Fact]
    public void An_answer_is_trusted_for_thirty_seconds_in_the_focused_window_and_two_in_any_other()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var focused = result.GetProperty("agedFocused");
        var unfocused = result.GetProperty("agedUnfocused");

        Assert.Equal(0, focused.GetProperty("asksWithin").GetInt32());
        Assert.Equal(1, focused.GetProperty("asksBeyond").GetInt32());
        Assert.Equal(1, focused.GetProperty("asks").GetInt32());
        Assert.Equal(["1:within", "1:beyond", "1:fresh again"], Strings(focused.GetProperty("wire")));
        Assert.Equal(0, unfocused.GetProperty("asksWithin").GetInt32());
        Assert.Equal(1, unfocused.GetProperty("asksBeyond").GetInt32());
        Assert.Equal(["1:within"], Strings(unfocused.GetProperty("wire")));
    }

    [Fact]
    public void A_question_that_gets_no_answer_keeps_the_connection_and_sends_what_waited_on_it()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var unanswered = result.GetProperty("unanswered");

        Assert.Equal(0, unanswered.GetProperty("reopens").GetInt32());
        Assert.Equal(0, unanswered.GetProperty("told").GetInt32());
        Assert.Equal(["1:click", "1:after"], Strings(unanswered.GetProperty("wire")));
        Assert.Equal(["1:click"], Strings(unanswered.GetProperty("emptyWire")));
        Assert.Equal(2, unanswered.GetProperty("asksAfterNextTrigger").GetInt32());
    }

    [Fact]
    public void A_slow_answer_holds_a_frame_for_a_moment_of_the_page_time_and_is_still_acted_on_when_it_comes()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var slow = result.GetProperty("slow");

        Assert.Empty(Strings(slow.GetProperty("wireWithin")));
        Assert.Equal(["1:click", "1:after"], Strings(slow.GetProperty("wireLate")));
        Assert.Equal(0, slow.GetProperty("reopens").GetInt32());
        Assert.Equal(["1:click", "2:held"], Strings(slow.GetProperty("changedWire")));
        Assert.Equal(1, slow.GetProperty("changedReopens").GetInt32());
    }

    [Fact]
    public void A_hold_that_lapses_after_its_answer_lets_nothing_go_twice()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var lapsed = result.GetProperty("lapsedAfterAnswer");

        Assert.Equal(["1:click"], Strings(lapsed.GetProperty("wire")));
        Assert.Equal(1, lapsed.GetProperty("asks").GetInt32());
    }

    [Fact]
    public void A_tab_told_to_ask_later_every_time_keeps_its_connection()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var later = result.GetProperty("laterForEver");

        Assert.Equal(0, later.GetProperty("reopens").GetInt32());
        Assert.Equal(6, later.GetProperty("asks").GetInt32());
    }

    [Fact]
    public void One_question_is_out_at_a_time_however_often_the_tab_is_told()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var asks = result.GetProperty("oneAtATime").GetProperty("asks").GetInt32();

        Assert.Equal(1, asks);
    }

    [Fact]
    public void Over_the_http_transport_nothing_is_asked_and_nothing_is_held()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var http = result.GetProperty("http");

        Assert.Equal(0, http.GetProperty("asks").GetInt32());
        Assert.Equal(["1:click"], Strings(http.GetProperty("wire")));
    }

    [Fact]
    public void A_tab_knows_whether_it_had_a_user_from_the_page_it_booted_from_and_from_each_answer()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var had = result.GetProperty("hadUser");

        Assert.True(had.GetProperty("booted").GetBoolean());
        Assert.False(had.GetProperty("bootedAnonymous").GetBoolean());
        Assert.True(had.GetProperty("afterAnswer").GetBoolean());
    }

    // What a tab learns because it was told, it does not tell on.
    [Fact]
    public void Three_tabs_open_their_connections_again_once_each_and_only_the_one_that_found_out_tells()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var tabs = result.GetProperty("threeTabs");

        Assert.Equal([1, 0, 0], tabs.GetProperty("told").EnumerateArray().Select(static e => e.GetInt32()));
        Assert.Equal([1, 1, 1], tabs.GetProperty("asks").EnumerateArray().Select(static e => e.GetInt32()));
        Assert.Equal([1, 1, 1], tabs.GetProperty("reopens").EnumerateArray().Select(static e => e.GetInt32()));
    }

    [Fact]
    public void A_tab_told_to_ask_later_holds_what_it_has_and_asks_again_without_opening_its_connection_again()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var later = result.GetProperty("later");
        var afterLater = later.GetProperty("afterLater");

        Assert.Equal(0, afterLater.GetProperty("reopens").GetInt32());
        Assert.Empty(Strings(afterLater.GetProperty("wire")));
        Assert.Equal(1, afterLater.GetProperty("waiting").GetInt32());
        Assert.Equal(2, later.GetProperty("asks").GetInt32());
        Assert.Equal(0, later.GetProperty("reopens").GetInt32());
        Assert.Equal(["1:click"], Strings(later.GetProperty("wire")));
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(static e => e.GetString()!)];
}
