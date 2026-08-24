using Rask.Native.Components;
using Rask.Native.Surface;

namespace Rask.Native.Tests.Surface;

/// <summary>
///     The rule a button's three appearance props obey, pinned where a build machine can reach it.
/// </summary>
/// <remarks>
///     <para>
///         #785: <c>Background</c> and <c>Color</c> were silently ignored on iOS and were discarded on
///         Android whenever <c>Style</c> was applied after them. The unit suite could not see it —
///         <c>FakeNativeSurface</c> asserts the emitted patch, and the patch was always right; the loss
///         happened inside the platform backend, which only a device or simulator exercises.
///     </para>
///     <para>
///         So the property worth pinning is not "what colour did UIKit paint" but "does the answer
///         depend on the order the props arrive in" — which is the actual defect, is platform-free, and
///         is what both backends now derive their paint from.
///     </para>
/// </remarks>
public sealed class NativeButtonAppearanceTests
{
    private const string Brand = "#7C3AED";
    private const string OnBrand = "#FFFFFF";

    public static TheoryData<NativePropId[]> EveryOrder()
    {
        var data = new TheoryData<NativePropId[]>();
        NativePropId[] props = [NativePropId.Style, NativePropId.Background, NativePropId.Color];
        foreach (var a in props)
        {
            foreach (var b in props)
            {
                foreach (var c in props)
                {
                    if (a != b && b != c && a != c)
                    {
                        data.Add([a, b, c]);
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryOrder))]
    public void The_same_three_writes_land_the_same_way_in_any_order(NativePropId[] order)
    {
        var appearance = new NativeButtonAppearance();

        foreach (var id in order)
        {
            var value = id switch
            {
                NativePropId.Style => NativePropValue.FromNumber((int)NativeButtonStyle.Filled),
                NativePropId.Background => NativePropValue.FromText(Brand),
                _ => NativePropValue.FromText(OnBrand),
            };

            Assert.True(appearance.Write(id, value, unset: false), $"{id} must be an appearance prop");
        }

        Assert.Equal(NativeButtonStyle.Filled, appearance.Style);
        Assert.Equal(Brand, appearance.Background);
        Assert.Equal(OnBrand, appearance.Foreground);
    }

    [Fact]
    public void A_style_written_last_does_not_discard_the_colours_written_before_it()
    {
        // The exact sequence from the report: an explicit brand pair, then the style. Applying each
        // prop as it arrived is what made this paint system blue.
        var appearance = new NativeButtonAppearance();
        appearance.Write(NativePropId.Background, NativePropValue.FromText(Brand), unset: false);
        appearance.Write(NativePropId.Color, NativePropValue.FromText(OnBrand), unset: false);
        appearance.Write(
            NativePropId.Style, NativePropValue.FromNumber((int)NativeButtonStyle.Destructive), unset: false);

        Assert.Equal(NativeButtonStyle.Destructive, appearance.Style);
        Assert.Equal(Brand, appearance.Background);
        Assert.Equal(OnBrand, appearance.Foreground);
    }

    [Fact]
    public void Clearing_a_colour_hands_the_decision_back_to_the_style()
    {
        var appearance = new NativeButtonAppearance();
        appearance.Write(NativePropId.Background, NativePropValue.FromText(Brand), unset: false);
        appearance.Write(NativePropId.Background, NativePropValue.Unset, unset: true);

        Assert.Null(appearance.Background);
        Assert.Equal(NativeButtonStyle.Filled, appearance.Style);
    }

    [Fact]
    public void Clearing_the_style_returns_it_to_filled_and_leaves_the_colours_alone()
    {
        var appearance = new NativeButtonAppearance();
        appearance.Write(
            NativePropId.Style, NativePropValue.FromNumber((int)NativeButtonStyle.Plain), unset: false);
        appearance.Write(NativePropId.Background, NativePropValue.FromText(Brand), unset: false);
        appearance.Write(NativePropId.Style, NativePropValue.Unset, unset: true);

        Assert.Equal(NativeButtonStyle.Filled, appearance.Style);
        Assert.Equal(Brand, appearance.Background);
    }

    [Fact]
    public void A_prop_that_does_not_decide_the_appearance_is_declined_so_the_caller_does_not_repaint()
    {
        var appearance = new NativeButtonAppearance();

        Assert.False(appearance.Write(NativePropId.Text, NativePropValue.FromText("Tap me"), unset: false));
        Assert.False(appearance.Write(NativePropId.Padding, NativePropValue.FromNumber(8), unset: false));
    }
}
