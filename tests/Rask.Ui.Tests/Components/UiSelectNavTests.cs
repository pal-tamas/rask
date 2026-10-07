namespace Rask.UiTests.Components;

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
        Assert.Equal(-1, UiSelectNav.LastEnabled(0, None));
    }

    [Fact]
    public void OptId_is_stable_per_index() =>
        Assert.Equal("uisel-7-opt-3", UiSelectNav.OptId("uisel-7", 3));
}
