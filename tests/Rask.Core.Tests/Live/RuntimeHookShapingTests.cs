using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The pure half of the runtime's behaviour hooks: what a mask makes of what was typed, which characters a
///     one-time-code cell takes, the triangle towards a submenu, and the attributes a hook holds against a render.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-field.ts</c>, <c>rask-otp.ts</c>, <c>rask-menu.ts</c> and <c>rask-owned.ts</c>
///     in a Node subprocess (<c>RuntimeHooksFixture.ts</c>). The expected strings are what Flux UI's live input
///     page produced for the same keys on 2026-10-07; the pointer, focus and top-layer half of every hook is
///     driven in a real browser by the <c>RuntimeHook*Tests</c> of <c>Rask.Server.E2E.Tests</c>.
/// </remarks>
public sealed class RuntimeHookShapingTests
{
    private static JsonElement? Run() => NodeFixture.Run("RuntimeHooksFixture");

    [Fact]
    public void A_pattern_drops_what_does_not_fit_and_writes_a_literal_only_before_a_character()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var pattern = root.GetProperty("pattern");

        Assert.Equal("(716) 123-4567", pattern.GetProperty("noisy").GetProperty("value").GetString());
        Assert.Equal("(7", pattern.GetProperty("one").GetString());
        Assert.Equal("(716", pattern.GetProperty("three").GetString());
        Assert.Equal("(716) 1", pattern.GetProperty("four").GetString());
        Assert.Equal("ab-12", pattern.GetProperty("letters").GetString());
        Assert.Equal("a1b2", pattern.GetProperty("either").GetString());
        Assert.Equal(string.Empty, pattern.GetProperty("empty").GetString());
    }

    [Fact]
    public void A_character_typed_in_the_middle_of_a_pattern_keeps_the_caret_behind_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var inserted = root.GetProperty("pattern").GetProperty("inserted");

        Assert.Equal("(715) 612-3456", inserted.GetProperty("value").GetString());
        Assert.Equal(4, inserted.GetProperty("caret").GetInt32());
    }

    [Fact]
    public void An_amount_is_grouped_as_it_is_typed_and_keeps_two_decimals()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var money = root.GetProperty("money");
        var steps = money.GetProperty("steps").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(["1", "123", "1,234", "1,234,567", "1,234,567.", "1,234,567.8", "1,234,567.89"], steps);
        Assert.Equal("-12.56", money.GetProperty("noisy").GetString());
        Assert.Equal("19,234,567.89", money.GetProperty("inserted").GetProperty("value").GetString());
        Assert.Equal(2, money.GetProperty("inserted").GetProperty("caret").GetInt32());
    }

    [Fact]
    public void An_amount_takes_its_marks_and_its_decimals_from_the_attribute()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var money = root.GetProperty("money");

        Assert.Equal("1.234.567,89", money.GetProperty("european").GetString());
        Assert.Equal("12,345", money.GetProperty("whole").GetString());
        Assert.Equal("7", money.GetProperty("zeros").GetString());
    }

    [Fact]
    public void A_one_time_code_keeps_only_the_characters_its_mode_accepts()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var otp = root.GetProperty("otp");

        Assert.Equal("987654", otp.GetProperty("numeric").GetString());
        Assert.Equal("abc", otp.GetProperty("alpha").GetString());
        Assert.Equal("a1b2", otp.GetProperty("alphanumeric").GetString());
    }

    [Fact]
    public void The_safe_area_is_the_triangle_from_the_pointer_to_the_near_edge_of_the_flyout()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var right = root.GetProperty("area").GetProperty("right");
        var left = root.GetProperty("area").GetProperty("left");

        Assert.Equal(100, right.GetProperty("left").GetDouble());
        Assert.Equal(40, right.GetProperty("top").GetDouble());
        Assert.Equal(100, right.GetProperty("width").GetDouble());
        Assert.Equal(100, right.GetProperty("height").GetDouble());
        Assert.Equal("polygon(0px 10px, 100px 0px, 100px 100px)", right.GetProperty("clip").GetString());
        Assert.Equal(200, left.GetProperty("left").GetDouble());
        Assert.Equal("polygon(100px 10px, 0px 0px, 0px 100px)", left.GetProperty("clip").GetString());
    }

    [Fact]
    public void A_key_list_names_keys_as_the_browser_does_with_Space_and_Arrows_as_its_two_words()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var listed = root.GetProperty("keys").GetProperty("listed").EnumerateArray().Select(e => e.GetBoolean()).ToArray();

        Assert.Equal([true, true, true, false, false], listed);
    }

    [Fact]
    public void A_roving_group_wraps_at_both_ends_and_a_list_scrolls_by_the_least_that_shows_the_row()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var keys = root.GetProperty("keys");

        Assert.Equal([0, 2, 2, -1], keys.GetProperty("rove").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.Equal([-30, 0, 20, 50], keys.GetProperty("scroll").EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    [Fact]
    public void A_search_finds_its_words_anywhere_in_a_row_whatever_the_case_or_the_accents_and_in_its_keywords()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var list = root.GetProperty("list");
        var search = list.GetProperty("search").EnumerateArray().Select(Words).ToArray();

        Assert.Equal(["osterreich", "cafe au lait"], Words(list.GetProperty("fold")));
        Assert.Equal(["Accounting", "Consulting"], search[0]);
        Assert.Equal(["Design services", "Web development", "Legal services", "Other"], search[1]);
        Assert.Equal(7, search[2].Length);
        Assert.Empty(search[3]);
        Assert.Equal(["Fruit"], Words(list.GetProperty("keywords")));
        Assert.Equal(["Something else"], Words(list.GetProperty("kept")));
    }

    [Fact]
    public void The_create_row_is_offered_once_what_is_typed_is_long_enough_and_names_no_option()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var offered = root.GetProperty("list").GetProperty("create").EnumerateArray().Select(e => e.GetBoolean()).ToArray();

        // "n", "ne", "security", "Sécurity", nothing typed; a list that always offers it; a list with no create row.
        Assert.Equal([false, true, false, false, false, true, false], offered);
    }

    [Fact]
    public void The_arrows_step_over_a_row_that_is_off_and_stop_at_either_end_and_letters_jump_to_the_row_that_starts_with_them()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var list = root.GetProperty("list");

        Assert.Equal([2, 2, 0, 1, 1], list.GetProperty("step").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        // "l" from no row: Legal services. "le" from there: still it. "d" from Design services: itself again, the
        // only one. "dd": the same letter twice. "zz": nobody's. "a" with Accounting off: nobody's.
        Assert.Equal([4, 4, 1, 1, -1, -1], list.GetProperty("ahead").EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    private static string[] Words(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    [Fact]
    public void A_pointer_becomes_a_fraction_of_the_track_inside_the_inset_and_never_leaves_zero_to_one()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var drag = root.GetProperty("drag");

        Assert.Equal([0, 0.25, 1, 0], drag.GetProperty("fractions").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        Assert.Equal(["0.3333", "1", "0"], drag.GetProperty("text").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void A_required_global_is_a_plain_identifier_the_scope_has_and_nothing_else()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var provided = root.GetProperty("requires").EnumerateArray().Select(e => e.GetBoolean()).ToArray();

        Assert.Equal([true, false, false, false], provided);
    }

    [Fact]
    public void Upload_progress_is_a_whole_percent_rounded_down_and_never_over_a_hundred()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var progress = root.GetProperty("progress").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(["0%", "12%", "99%", "100%"], progress);
    }

    [Fact]
    public void The_row_nearest_the_pointer_changes_at_the_midpoint_and_a_tooltip_flips_when_it_would_leave_the_frame()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var plot = root.GetProperty("plot");

        Assert.Equal([0, 0, 1, -1], plot.GetProperty("nearest").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.Equal([60, 400 - 80, 311 - 10 - 80], plot.GetProperty("beside").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.Equal("300 100", plot.GetProperty("size").GetString());
    }

    [Fact]
    public void A_plots_text_reads_its_rows_line_and_a_box_half_a_pixel_off_is_the_box_the_field_says()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var plot = root.GetProperty("plot");

        Assert.Equal(["a", "", "c", "", ""], plot.GetProperty("lines").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal([true, true, false, true, false], plot.GetProperty("same").EnumerateArray().Select(e => e.GetBoolean()).ToArray());
    }

    [Fact]
    public void A_digit_no_second_could_follow_finishes_its_part_and_one_too_many_starts_the_part_again()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var segments = root.GetProperty("segments");
        string?[] Texts(string name) => segments.GetProperty(name).EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(["03.", "01", "12.", "03."], Texts("month"));
        Assert.Equal(["01.", "00"], Texts("zeros"));
        Assert.Equal(["02", "03.", "23.", "06."], Texts("hour"));
        Assert.Equal(["0026", "0206", "2026."], Texts("year"));
    }

    [Fact]
    public void A_short_year_lands_within_twenty_years_ahead_and_a_pasted_date_is_read_in_the_order_of_the_parts()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var segments = root.GetProperty("segments");
        string?[] Texts(string name) => segments.GetProperty(name).EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(["2026", "2046", "1947", "2005", "0202"], Texts("pivot"));
        Assert.Equal([29, 28, 29, 28, 30, 31], segments.GetProperty("days").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.Equal(["2026-03-14", "2026-03-14", "2026-03-14", null, null], Texts("pasted"));
    }

    [Fact]
    public void A_hook_holds_one_attribute_of_one_element_until_it_lets_go()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var owned = root.GetProperty("owned");

        Assert.True(owned.GetProperty("held").GetBoolean());
        Assert.False(owned.GetProperty("otherName").GetBoolean());
        Assert.False(owned.GetProperty("otherElement").GetBoolean());
        Assert.True(owned.GetProperty("released").GetBoolean());
        Assert.True(owned.GetProperty("checked").GetBoolean());
    }

    // A landing as the fixture writes it: x, y and the height it is held to, "-" when it is not.
    private static string Landed(JsonElement landing) =>
        string.Join(' ', landing.EnumerateArray().Select(part => part.ValueKind == JsonValueKind.Null ? "-" : part.GetDouble().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void A_panel_with_room_lands_where_it_asked_and_one_without_takes_the_other_side_or_the_other_alignment()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var place = root.GetProperty("place");
        var over = place.GetProperty("over").EnumerateArray().Select(Landed).ToArray();

        Assert.Equal("800 445 -", Landed(place.GetProperty("fits")));
        // Its bottom edge on the viewport's still fits, inside the 5 px a held height keeps — and a pixel lower
        // it goes over the trigger.
        Assert.Equal(["800 545 -", "800 790 285", "800 451 -"], over);
        Assert.Equal("1708 445 -", Landed(place.GetProperty("end")));
        Assert.Equal("1694 645 -", Landed(place.GetProperty("corner")));
        Assert.Equal("40 445 -", Landed(place.GetProperty("start")));
    }

    [Fact]
    public void A_panel_within_five_pixels_of_an_edge_is_slid_back_and_one_wider_than_the_viewport_keeps_its_start_in_view()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var place = root.GetProperty("place");
        var phone = place.GetProperty("phone").EnumerateArray().Select(Landed).ToArray();

        Assert.Equal("1723 445 -", Landed(place.GetProperty("slid")));
        Assert.Equal("5 445 -", Landed(place.GetProperty("centred")));
        Assert.Equal("5 445 -", Landed(place.GetProperty("wide")));
        Assert.Equal(["193 345 -", "5 345 -"], phone);
    }

    [Fact]
    public void An_offset_turns_with_the_alignment_and_a_panel_beside_its_trigger_turns_to_the_other_side()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var place = root.GetProperty("place");
        var offset = place.GetProperty("offset").EnumerateArray().Select(Landed).ToArray();
        var beside = place.GetProperty("beside").EnumerateArray().Select(Landed).ToArray();

        Assert.Equal(["832 445 -", "1691 445 -"], offset);
        Assert.Equal(["931 275 -", "1592 275 -", "931 785 -"], beside);
        Assert.Equal("734 445 -", Landed(place.GetProperty("rtl")));
    }

    [Fact]
    public void A_panel_taller_than_the_room_takes_the_roomier_side_and_is_held_to_what_is_left_five_pixels_from_the_edge()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var place = root.GetProperty("place");
        var tall = place.GetProperty("tall").EnumerateArray().Select(Landed).ToArray();
        var brim = place.GetProperty("brim").EnumerateArray().Select(Landed).ToArray();

        Assert.Equal(["800 445 630", "800 5 530", "931 5 1070"], tall);
        // What the hook then does with a height the panel barely scrolls in is a browser's to show
        // (RuntimeHookPlaceTests): this is the height it is offered.
        Assert.Equal(["800 5 290", "800 5 290", "800 6 -"], brim);
    }

    [Fact]
    public void A_menu_at_the_pointer_reaches_back_from_it_and_opens_over_it_at_the_bottom_of_the_viewport()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var pointer = root.GetProperty("place").GetProperty("pointer").EnumerateArray().Select(Landed).ToArray();

        // At the left edge its end on the pointer would put it out of view: its start goes there instead. And
        // one taller than the viewport, which is never held to a height, runs past the bottom edge.
        Assert.Equal(["400 305 -", "100 955 -", "400 305 -"], pointer);
    }

    [Fact]
    public void A_button_says_it_copied_for_as_long_as_Flux_shows_its_tick()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var shown = root.GetProperty("copiedMs").GetInt32();

        Assert.Equal(2000, shown);
    }
}
