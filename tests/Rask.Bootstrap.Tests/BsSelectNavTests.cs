namespace Rask.Bootstrap.Tests;

// Unit tests for the shared roving-cursor math that both BsSelect and BsMultiSelect drive (BsSelectNav).
// The `disabled` predicate lets the cursor skip per-option-disabled indices; -1 means "nothing selectable".
public class BsSelectNavTests
{
    private static Func<int, bool> Disabled(params int[] idx) => i => idx.Contains(i);
    private static readonly Func<int, bool> None = _ => false;

    [Fact]
    public void OptId_Formats_PrefixOptIndex() =>
        Assert.Equal("x-opt-3", BsSelectNav.OptId("x", 3));

    [Fact]
    public void FirstEnabled_SkipsLeadingDisabled()
    {
        Assert.Equal(0, BsSelectNav.FirstEnabled(3, None));
        Assert.Equal(2, BsSelectNav.FirstEnabled(4, Disabled(0, 1)));
        Assert.Equal(-1, BsSelectNav.FirstEnabled(2, Disabled(0, 1))); // all disabled
        Assert.Equal(-1, BsSelectNav.FirstEnabled(0, None));           // empty
    }

    [Fact]
    public void LastEnabled_SkipsTrailingDisabled()
    {
        Assert.Equal(2, BsSelectNav.LastEnabled(3, None));
        Assert.Equal(1, BsSelectNav.LastEnabled(4, Disabled(2, 3)));
        Assert.Equal(-1, BsSelectNav.LastEnabled(2, Disabled(0, 1)));
    }

    [Fact]
    public void Step_MovesToNextEnabled_SkippingDisabled()
    {
        Assert.Equal(1, BsSelectNav.Step(0, 1, 3, None));
        Assert.Equal(2, BsSelectNav.Step(0, 1, 3, Disabled(1)));   // skip the disabled 1
        Assert.Equal(0, BsSelectNav.Step(1, -1, 3, None));
    }

    [Fact]
    public void Step_StaysPut_WhenNoEnabledInThatDirection()
    {
        Assert.Equal(2, BsSelectNav.Step(2, 1, 3, None));            // already last → stay (no wrap)
        Assert.Equal(0, BsSelectNav.Step(0, -1, 3, None));          // already first → stay
        Assert.Equal(0, BsSelectNav.Step(0, 1, 3, Disabled(1, 2))); // nothing enabled ahead → stay
    }

    [Fact]
    public void Seed_PrefersSelected_ElseFirstEnabled()
    {
        Assert.Equal(2, BsSelectNav.Seed(2, 4, None));             // selection in range + enabled
        Assert.Equal(0, BsSelectNav.Seed(-1, 4, None));            // no selection → first enabled
        Assert.Equal(1, BsSelectNav.Seed(0, 4, Disabled(0)));     // selection disabled → first enabled
        Assert.Equal(-1, BsSelectNav.Seed(0, 2, Disabled(0, 1))); // everything disabled → -1
    }

    [Fact]
    public void Normalize_ClampsAndSnapsOffDisabled()
    {
        Assert.Equal(2, BsSelectNav.Normalize(9, 3, None));         // past the end → last
        Assert.Equal(0, BsSelectNav.Normalize(-4, 3, None));       // below 0 → first
        Assert.Equal(1, BsSelectNav.Normalize(1, 3, None));        // in range + enabled → unchanged
        Assert.Equal(-1, BsSelectNav.Normalize(0, 0, None));       // empty list → -1
        Assert.Equal(2, BsSelectNav.Normalize(1, 4, Disabled(1))); // on a disabled option → next enabled
        Assert.Equal(0, BsSelectNav.Normalize(2, 3, Disabled(1, 2))); // none ahead → nearest enabled behind
    }
}
