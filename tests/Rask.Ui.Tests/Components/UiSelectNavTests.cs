namespace Rask.Ui.Tests.Components;

/// <summary>
///     The roving-cursor arithmetic behind the non-native listbox.
/// </summary>
/// <remarks>
///     Tested directly rather than through the component because this is where a control of this kind
///     actually goes wrong — an off-by-one in the flat index, a disabled option the cursor lands on, a
///     wrap-around nobody asked for — and none of it is visible in rendered markup.
/// </remarks>
public sealed class UiSelectNavTests
{
    private static readonly Func<int, bool> None = _ => false;

    [Fact]
    public void Step_moves_one_option()
    {
        Assert.Equal(1, UiSelectNav.Step(0, 1, 3, None));
        Assert.Equal(0, UiSelectNav.Step(1, -1, 3, None));
    }

    [Fact]
    public void Step_does_not_wrap_around()
    {
        // ArrowDown at the last option is a no-op, not a jump back to the first. A wrap is disorienting
        // in a list you cannot see all of at once.
        Assert.Equal(2, UiSelectNav.Step(2, 1, 3, None));
        Assert.Equal(0, UiSelectNav.Step(0, -1, 3, None));
    }

    [Fact]
    public void Step_skips_over_a_disabled_option()
    {
        // Landing on one and refusing to act would look like a broken key.
        Assert.Equal(2, UiSelectNav.Step(0, 1, 3, i => i == 1));
    }

    [Fact]
    public void Step_stays_put_when_everything_beyond_is_disabled() =>
        Assert.Equal(0, UiSelectNav.Step(0, 1, 3, i => i > 0));

    [Fact]
    public void FirstEnabled_and_LastEnabled_skip_the_disabled_ends()
    {
        Assert.Equal(1, UiSelectNav.FirstEnabled(3, i => i == 0));
        Assert.Equal(1, UiSelectNav.LastEnabled(3, i => i == 2));
    }

    [Fact]
    public void An_all_disabled_list_has_no_landing_place()
    {
        // -1, not 0: a cursor at 0 over a disabled option would put aria-activedescendant on something
        // that cannot be chosen.
        Assert.Equal(-1, UiSelectNav.FirstEnabled(3, _ => true));
        Assert.Equal(-1, UiSelectNav.LastEnabled(3, _ => true));
    }

    [Fact]
    public void An_empty_list_has_no_landing_place()
    {
        Assert.Equal(-1, UiSelectNav.FirstEnabled(0, None));
        Assert.Equal(-1, UiSelectNav.Normalize(0, 0, None));
    }

    [Fact]
    public void Seed_opens_on_the_selected_option()
    {
        // Opening the list should put the cursor where the reader already is.
        Assert.Equal(2, UiSelectNav.Seed(2, 3, None));
    }

    [Fact]
    public void Seed_falls_back_to_the_first_enabled_option()
    {
        Assert.Equal(0, UiSelectNav.Seed(-1, 3, None));
        Assert.Equal(1, UiSelectNav.Seed(0, 3, i => i == 0));
    }

    [Fact]
    public void Normalize_clamps_a_stale_cursor_back_into_the_list()
    {
        Assert.Equal(0, UiSelectNav.Normalize(-5, 3, None));
        Assert.Equal(2, UiSelectNav.Normalize(99, 3, None));
    }

    [Fact]
    public void Normalize_moves_off_a_disabled_option()
    {
        // Prefers forward, then backward — so a cursor left on an option that has since become
        // unavailable lands somewhere selectable rather than nowhere.
        Assert.Equal(2, UiSelectNav.Normalize(1, 3, i => i == 1));
        Assert.Equal(0, UiSelectNav.Normalize(2, 3, i => i >= 1));
    }

    [Fact]
    public void OptId_is_stable_per_index() =>
        Assert.Equal("uisel-7-opt-3", UiSelectNav.OptId("uisel-7", 3));

    [Fact]
    public void Build_without_grouping_keeps_one_headerless_group()
    {
        var layout = UiSelectNav.Build<string>(["a", "b"], null);

        Assert.Single(layout.Groups);
        Assert.Null(layout.Groups[0].Header);
        Assert.Equal(2, layout.Flat.Count);
    }

    [Fact]
    public void Grouping_renumbers_the_flat_index_into_RENDER_order()
    {
        // The whole point of the flat space: options are bucketed by group, and each gets the next flat
        // index IN THAT ORDER — so an arrow key moves to the next option the reader can see, not to the
        // next one in the caller's original list.
        var layout = UiSelectNav.Build<string>(["a1", "b1", "a2"], v => v[..1]);

        Assert.Equal(2, layout.Groups.Count);
        Assert.Equal(["a1", "a2", "b1"], layout.Flat);
        Assert.Equal([0, 1], layout.Groups[0].Rows.Select(r => r.FlatIndex));
        Assert.Equal([2], layout.Groups[1].Rows.Select(r => r.FlatIndex));
    }

    [Fact]
    public void Groups_keep_first_seen_order() =>
        Assert.Equal(
            ["b", "a"],
            UiSelectNav.Build<string>(["b1", "a1"], v => v[..1]).Groups.Select(g => g.Header));
}
