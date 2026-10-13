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
    public void A_question_that_gets_no_answer_opens_the_connection_again_rather_than_sending()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var unanswered = result.GetProperty("unanswered");

        Assert.Equal(1, unanswered.GetProperty("reopens").GetInt32());
        Assert.Empty(Strings(unanswered.GetProperty("wire")));
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

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(static e => e.GetString()!)];
}
