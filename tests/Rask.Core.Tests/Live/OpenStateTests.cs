using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     A popover, a dialog or a details the reader just opened or shut stays as the reader left it until the page
///     has answered that toggle, and what the runtime opens or shuts for a render is not reported back.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-owned.ts</c>, <c>rask-events.ts</c> and <c>rask-dom.ts</c> in a Node
///     subprocess (<c>OpenStateFixture.ts</c>): the fixture is the server, replying and acking in the order of
///     the failing run. Before this, a reply rendered before the server heard Escape opened the list again, its
///     toggle was followed as the reader's, and the two chased each other for as long as the page was up.
/// </remarks>
public sealed class OpenStateTests
{
    private static JsonElement? Run() => NodeFixture.Run("OpenStateFixture");

    private static string[] Sent(JsonElement of, string name = "sent") =>
        [.. of.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];

    [Fact]
    public void A_reply_rendered_before_Escape_was_heard_leaves_the_list_shut_and_no_toggle_of_the_runtime_is_sent()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var reported = root.GetProperty("reported");

        Assert.True(reported.GetProperty("shutBeforeEscapeWasSent").GetBoolean());
        Assert.True(reported.GetProperty("shutBeforeEscapeWasAnswered").GetBoolean());
        Assert.True(reported.GetProperty("shut").GetBoolean());
        Assert.Equal(["toggle:open", "click", "toggle:closed"], Sent(reported));
    }

    [Fact]
    public void Replies_older_than_the_readers_last_toggle_do_not_open_what_the_reader_shut()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var stale = root.GetProperty("stale");

        Assert.True(stale.GetProperty("shutUnderStaleReplies").GetBoolean());
        Assert.True(stale.GetProperty("shut").GetBoolean());
        Assert.Equal(["toggle:open", "toggle:closed"], Sent(stale));
    }

    [Fact]
    public void The_answer_to_the_readers_toggle_is_carried_out_once_it_is_in()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var answer = root.GetProperty("answer");

        Assert.True(answer.GetProperty("openUntilAnswered").GetBoolean());
        Assert.True(answer.GetProperty("shut").GetBoolean());
        Assert.Equal(["toggle:open"], Sent(answer));
    }

    [Fact]
    public void A_popover_the_page_opens_and_shuts_tells_the_page_nothing_and_the_readers_next_toggle_is_sent()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var ours = root.GetProperty("ours");

        Assert.True(ours.GetProperty("openedByThePage").GetBoolean());
        Assert.True(ours.GetProperty("shutByThePage").GetBoolean());
        Assert.Empty(Sent(ours, "told"));
        Assert.Equal(["toggle:open"], Sent(ours, "readers"));
    }

    [Fact]
    public void A_popover_the_page_opened_and_the_reader_shut_at_once_says_closed_in_its_one_toggle()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "coalesced");

        Assert.Equal(["toggle:closed"], sent);
    }

    [Fact]
    public void An_opening_that_was_refused_and_a_popover_nobody_hears_hold_nothing_against_the_next_render()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var refused = root.GetProperty("refusedOpeningLetsGo").GetBoolean();
        var silent = root.GetProperty("silentIsDriven").GetBoolean();

        Assert.True(refused);
        Assert.True(silent);
    }

    [Fact]
    public void A_details_the_reader_opened_keeps_its_open_attribute_until_the_page_has_answered()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var details = root.GetProperty("details");

        Assert.True(details.GetProperty("keptOpen").GetBoolean());
        Assert.True(details.GetProperty("shutOnceAnswered").GetBoolean());
        Assert.Equal(["toggle:open"], Sent(details));
        Assert.True(details.GetProperty("writtenWhenNothingIsOwed").GetBoolean());
        Assert.False(details.GetProperty("untouchedAttribute").GetBoolean());
    }

    [Fact]
    public void A_dialog_the_runtime_closed_does_not_report_its_close_and_one_the_reader_closed_does()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var dialog = root.GetProperty("dialog");

        Assert.Empty(Sent(dialog, "runtimeClose"));
        Assert.Equal(["close"], Sent(dialog, "readerClose"));
    }
}
