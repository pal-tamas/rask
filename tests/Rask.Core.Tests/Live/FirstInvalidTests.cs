using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     A submit the page refused takes the reader to the first invalid field once its render is on the page — and a
///     submit that was not refused, or a reader who has moved on, keeps focus where it is.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-first-invalid.ts</c> on Jint against a stub form (<c>FirstInvalidFixture.ts</c>).
///     The hosts' wiring — the refusal count the form renders, the moment each host asks — is covered in a real
///     browser by <c>FirstInvalidFieldTests</c> (Server) and the site's form-tabs journey (WASM).
/// </remarks>
public sealed class FirstInvalidTests
{
    private static JsonElement Run() => ScriptFixture.Run("FirstInvalidFixture");

    [Fact]
    public void A_refused_submit_focuses_the_first_invalid_field_in_document_order_and_scrolls_it_to_the_middle()
    {
        var root = Run();

        var refused = root.GetProperty("refused");

        Assert.Equal("email", refused.GetProperty("focus").GetString());
        Assert.Equal("center", refused.GetProperty("scrolled").GetProperty("block").GetString());
        Assert.Equal("smooth", refused.GetProperty("scrolled").GetProperty("behavior").GetString());
    }

    [Fact]
    public void A_submit_that_was_not_refused_moves_no_focus_whatever_is_still_marked()
    {
        var root = Run();

        var focus = root.GetProperty("notRefused").GetString();

        Assert.Equal("save", focus);
    }

    [Fact]
    public void A_reader_who_asked_for_less_motion_is_shown_the_field_at_once()
    {
        var root = Run();

        var reduced = root.GetProperty("reduced");

        Assert.Equal("auto", reduced.GetProperty("behavior").GetString());
    }

    [Fact]
    public void A_reader_who_moved_focus_while_the_submit_was_in_flight_keeps_it()
    {
        var root = Run();

        var focus = root.GetProperty("movedOn").GetString();

        Assert.Equal("other", focus);
    }

    [Fact]
    public void Focus_the_held_form_lost_to_the_page_is_not_the_reader_moving_on()
    {
        var root = Run();

        var focus = root.GetProperty("fromBody").GetString();

        Assert.Equal("email", focus);
    }

    [Fact]
    public void A_field_not_on_screen_disabled_or_inert_is_passed_over()
    {
        var root = Run();

        var focus = root.GetProperty("passedOver").GetString();

        Assert.Equal("shown-one", focus);
    }

    [Fact]
    public void A_field_on_the_tab_the_refusal_opens_is_looked_for_after_the_tab_is_shown()
    {
        var root = Run();

        var focus = root.GetProperty("tabShown").GetString();

        Assert.Equal("on-refused-tab", focus);
    }

    [Fact]
    public void A_kit_control_marked_on_its_wrapper_hands_focus_to_the_first_thing_inside_it_that_takes_it()
    {
        var root = Run();

        var focus = root.GetProperty("wrapped").GetString();

        Assert.Equal("select-trigger", focus);
    }

    [Fact]
    public void A_refusal_with_no_field_that_can_take_focus_leaves_focus_alone()
    {
        var root = Run();

        var focus = root.GetProperty("nothing").GetString();

        Assert.Equal("save", focus);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("noneFirst").ValueKind);
    }

    [Fact]
    public void A_form_that_went_away_with_its_page_focuses_nothing()
    {
        var root = Run();

        var gone = root.GetProperty("gone");

        Assert.False(gone.GetProperty("took").GetBoolean());
        Assert.Equal("save", gone.GetProperty("focus").GetString());
    }
}
