using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What a navigation's page does for a reader who cannot see it arrive, on both hosts: where keyboard focus goes,
///     what the polite live region says, and what is scrolled.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-route-focus.ts</c> on Jint against a stub page (<c>RouteFocusFixture.ts</c>).
///     The hosts' wiring is covered in a real browser by <c>AutofocusJourneyTests</c> (Server) and the site's
///     navigation journey (WASM).
/// </remarks>
public sealed class RouteFocusTests
{
    private static JsonElement Run() => ScriptFixture.Run("RouteFocusFixture");

    private static (string? At, string? After) Heard(JsonElement heard) =>
        (heard.GetProperty("at").GetString(), heard.GetProperty("after").GetString());

    [Fact]
    public void The_live_region_is_polite_atomic_and_left_alone_by_the_morph()
    {
        var root = Run();

        var region = root.GetProperty("region");

        Assert.Equal("polite", region.GetProperty("live").GetString());
        Assert.Equal("true", region.GetProperty("atomic").GetString());
        Assert.True(region.GetProperty("managed").GetBoolean());
        Assert.Equal("rask-route-announcer", region.GetProperty("className").GetString());
    }

    [Fact]
    public void A_forward_navigation_focuses_the_main_content_announces_the_title_and_scrolls_to_the_top()
    {
        var root = Run();

        var forward = root.GetProperty("forward");

        Assert.Equal("main", forward.GetProperty("focus").GetString());
        Assert.Equal(("", "Orders | Shop"), Heard(forward.GetProperty("heard")));
        Assert.Equal(1, forward.GetProperty("scrolledTop").GetInt32());
        Assert.Equal("-1", forward.GetProperty("tabindex").GetString());
    }

    [Fact]
    public void The_tabindex_the_main_content_was_given_for_the_focus_goes_when_focus_leaves_it()
    {
        var root = Run();

        var after = root.GetProperty("forwardAfterBlur");

        Assert.Equal(JsonValueKind.Null, after.ValueKind);
    }

    [Fact]
    public void A_main_content_that_is_a_tab_stop_of_its_own_keeps_its_tabindex()
    {
        var root = Run();

        var own = root.GetProperty("own");

        Assert.Equal("main", own.GetProperty("focus").GetString());
        Assert.Equal("0", own.GetProperty("tabindex").GetString());
    }

    [Fact]
    public void A_page_without_main_content_focuses_its_heading()
    {
        var root = Run();

        var focus = root.GetProperty("headingFocus").GetString();

        Assert.Equal("heading", focus);
    }

    [Fact]
    public void A_page_with_neither_main_content_nor_a_heading_leaves_focus_and_is_still_announced()
    {
        var root = Run();

        var bare = root.GetProperty("bare");

        Assert.Equal("pressed", bare.GetProperty("focus").GetString());
        Assert.Equal(("", "Bare | Shop"), Heard(bare.GetProperty("heard")));
    }

    [Fact]
    public void A_page_without_a_title_is_announced_by_its_address()
    {
        var root = Run();

        var untitled = Heard(root.GetProperty("untitled"));

        Assert.Equal(("", "/orders/7"), untitled);
    }

    [Fact]
    public void A_link_to_a_fragment_scrolls_to_and_focuses_its_element_and_a_missing_one_falls_back_to_the_page()
    {
        var root = Run();

        var fragment = root.GetProperty("fragment");
        var missing = root.GetProperty("missing");

        Assert.Equal("shipping", fragment.GetProperty("focus").GetString());
        Assert.True(fragment.GetProperty("scrolledTo").GetBoolean());
        Assert.Equal(0, fragment.GetProperty("scrolledTop").GetInt32());
        Assert.Equal("main", missing.GetProperty("focus").GetString());
        Assert.Equal(1, missing.GetProperty("scrolledTop").GetInt32());
    }

    [Fact]
    public void A_fragment_that_is_no_selector_is_still_found_by_its_id()
    {
        var root = Run();

        var digits = root.GetProperty("digits").GetString();

        Assert.Equal("1", digits);
    }

    [Fact]
    public void A_page_with_an_autofocus_field_focuses_the_field_and_leaves_the_main_content_untouched()
    {
        var root = Run();

        var autofocused = root.GetProperty("autofocused");

        Assert.Equal("name", autofocused.GetProperty("focus").GetString());
        Assert.Equal(JsonValueKind.Null, autofocused.GetProperty("tabindex").ValueKind);
    }

    [Fact]
    public void A_link_to_the_page_the_reader_is_on_focuses_its_main_content_again()
    {
        var root = Run();

        var focus = root.GetProperty("samePage").GetString();

        Assert.Equal("main", focus);
    }

    [Fact]
    public void Two_navigations_inside_the_pause_announce_only_the_newer_page()
    {
        var root = Run();

        var quick = root.GetProperty("quick");

        // 60 ms after the first, 40 after the second: the first's title would have been written by now.
        Assert.Equal("", quick.GetProperty("middle").GetString());
        Assert.Equal("Second | Shop", quick.GetProperty("after").GetString());
    }

    [Fact]
    public void A_replaced_address_nobody_went_back_for_says_nothing_and_moves_nothing()
    {
        var root = Run();

        var replaced = root.GetProperty("replaced");

        Assert.Equal(("unchanged", "unchanged"), Heard(replaced.GetProperty("heard")));
        Assert.Equal("pressed", replaced.GetProperty("focus").GetString());
        Assert.Equal(0, replaced.GetProperty("scrolledTop").GetInt32());
    }

    [Fact]
    public void Back_announces_its_page_and_leaves_focus_and_scroll_to_the_browser()
    {
        var root = Run();

        var back = root.GetProperty("back");

        Assert.Equal(("unchanged", "unchanged"), Heard(back.GetProperty("between")));
        Assert.Equal(("", "Orders | Shop"), Heard(back.GetProperty("heard")));
        Assert.Equal("pressed", back.GetProperty("focus").GetString());
        Assert.Equal(0, back.GetProperty("scrolledTop").GetInt32());
        Assert.Equal(("unchanged", "unchanged"), Heard(root.GetProperty("afterBack")));
    }

    [Fact]
    public void A_render_of_the_open_page_says_nothing_and_moves_nothing()
    {
        var root = Run();

        var render = root.GetProperty("render");

        Assert.Equal(("unchanged", "unchanged"), Heard(render.GetProperty("heard")));
        Assert.Equal("pressed", render.GetProperty("focus").GetString());
        Assert.Equal(0, render.GetProperty("scrolledTop").GetInt32());
    }
}
