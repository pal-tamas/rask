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
