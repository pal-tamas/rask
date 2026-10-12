using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     Which <c>autofocus</c> element a frame the runtime just applied gives focus to, and when it gives it to none.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-autofocus.ts</c> in a Node subprocess against a stub page
///     (<c>AutofocusFixture.ts</c>). The hosts' wiring — after which frame it is asked, and what the route's own
///     focus does then — is covered in a real browser by <c>AutofocusJourneyTests</c>.
/// </remarks>
public sealed class AutofocusTests
{
    private static JsonElement? Run() => NodeFixture.Run("AutofocusFixture");

    [Fact]
    public void What_the_document_was_served_with_is_the_browsers_to_focus_and_no_render_focuses_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var served = root.GetProperty("served");

        Assert.False(served.GetProperty("took").GetBoolean());
        Assert.Equal(JsonValueKind.Null, served.GetProperty("focus").ValueKind);
    }

    [Fact]
    public void A_navigation_focuses_the_first_autofocus_element_on_screen_and_enabled_even_one_the_frame_kept()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var navigation = root.GetProperty("navigation");

        // Before it in the document: one not on screen, one disabled, one inside an inert subtree.
        Assert.True(navigation.GetProperty("took").GetBoolean());
        Assert.Equal("served", navigation.GetProperty("focus").GetString());
        Assert.True(navigation.GetProperty("scrolls").GetBoolean());
    }

    [Fact]
    public void A_navigation_to_a_page_with_no_autofocus_element_takes_no_focus_so_the_routes_own_goes_ahead()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var none = root.GetProperty("none");

        Assert.False(none.GetProperty("took").GetBoolean());
        Assert.Equal(JsonValueKind.Null, none.GetProperty("focus").ValueKind);
    }

    [Fact]
    public void A_page_reached_at_a_fragment_keeps_focus_for_the_fragment_and_owes_its_autofocus_element_none_later()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var fragment = root.GetProperty("fragment");

        Assert.False(fragment.GetProperty("took").GetBoolean());
        Assert.Equal(JsonValueKind.Null, fragment.GetProperty("focus").ValueKind);
        Assert.False(fragment.GetProperty("laterTook").GetBoolean());
    }

    [Fact]
    public void An_element_that_arrives_with_autofocus_is_focused_once_and_one_that_keeps_it_is_not_focused_again()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var arrival = root.GetProperty("arrival");

        Assert.True(arrival.GetProperty("took").GetBoolean());
        Assert.Equal("row", arrival.GetProperty("focus").GetString());
        Assert.True(arrival.GetProperty("scrolls").GetBoolean());
        Assert.False(arrival.GetProperty("againTook").GetBoolean());
        Assert.Equal("button", arrival.GetProperty("againFocus").GetString());
    }

    [Fact]
    public void An_arrival_takes_nothing_from_a_field_the_reader_is_typing_in_and_is_not_focused_afterwards_either()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var typing = root.GetProperty("whileTyping");

        foreach (var field in typing.EnumerateObject())
        {
            Assert.True(field.Value.GetString() == "on", $"focus was taken from a {field.Name} field");
        }

        Assert.False(root.GetProperty("afterTypingTook").GetBoolean());
    }

    [Fact]
    public void An_arrival_is_focused_from_a_control_that_takes_no_text()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var controls = root.GetProperty("whileNotTyping");

        foreach (var control in controls.EnumerateObject())
        {
            Assert.True(control.Value.GetBoolean(), $"an arrival was not focused from a {control.Name}");
        }
    }

    [Fact]
    public void An_element_that_arrives_off_screen_is_left_to_whatever_shows_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var closed = root.GetProperty("closed");

        // A closed dialog's field: the browser focuses it when the dialog opens, and the runtime does not as well.
        Assert.False(closed.GetProperty("took").GetBoolean());
        Assert.False(closed.GetProperty("openedTook").GetBoolean());
        Assert.Equal("button", closed.GetProperty("openedFocus").GetString());
    }

    [Fact]
    public void Back_focuses_only_what_arrived_and_leaves_the_scroll_to_the_browser()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var back = root.GetProperty("back");

        Assert.True(back.GetProperty("took").GetBoolean());
        Assert.Equal("back", back.GetProperty("focus").GetString());
        Assert.True(back.GetProperty("keepsScroll").GetBoolean());
    }
}
