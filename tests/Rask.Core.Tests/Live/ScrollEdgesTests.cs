using System.Text.Json;

namespace Rask.Core.Tests.Live;

// The runtime's scroll edges: a table's scroll area is marked `data-scrolled-right` while something is hidden past its
// left edge and `data-scrolled-left` while something is hidden past its right one — Flux's marks, which a sticky
// column's shadow is drawn on.
//
// It is a behaviour hook (rask-scroll-edges.ts) because only the browser knows how far a box has scrolled. The rule is
// a pure function, driven here in-process on Jint; that the marks land and draw the shadow is the site's E2E
// (UiKitDataDisplayTests).
public sealed class ScrollEdgesTests
{
    [Fact]
    public void An_area_at_its_start_hides_only_what_lies_past_its_right_edge()
    {
        var said = ScriptFixture.Run("ScrollEdgesFixture");

        var start = Edges(said, "atStart");

        Assert.Equal((false, true), start);
    }

    [Fact]
    public void An_area_scrolled_part_way_hides_something_past_both_edges()
    {
        var said = ScriptFixture.Run("ScrollEdgesFixture");

        var (middle, justIn, almostDone) = (Edges(said, "inTheMiddle"), Edges(said, "aPixelIn"), Edges(said, "aPixelShortOfTheEnd"));

        Assert.Equal((true, true), middle);
        Assert.Equal((true, true), justIn);
        Assert.Equal((true, true), almostDone);
    }

    [Fact]
    public void An_area_scrolled_to_its_end_hides_only_what_lies_past_its_left_edge()
    {
        var said = ScriptFixture.Run("ScrollEdgesFixture");

        var end = Edges(said, "atTheEnd");

        Assert.Equal((true, false), end);
    }

    [Fact]
    public void Less_than_a_pixel_from_an_edge_counts_as_at_it_as_a_zoomed_page_scrolls()
    {
        var said = ScriptFixture.Run("ScrollEdgesFixture");

        var (start, end, fraction) = (Edges(said, "withinSlackOfTheStart"), Edges(said, "withinSlackOfTheEnd"), Edges(said, "fitsButForAFraction"));

        Assert.Equal((false, true), start);
        Assert.Equal((true, false), end);
        Assert.Equal((false, false), fraction);
    }

    [Fact]
    public void An_area_whose_table_fits_hides_nothing()
    {
        var said = ScriptFixture.Run("ScrollEdgesFixture");

        var (fits, wider, shrunk) = (Edges(said, "fits"), Edges(said, "widerThanItsTable"), Edges(said, "shrunkToFit"));

        Assert.Equal((false, false), fits);
        Assert.Equal((false, false), wider);
        Assert.Equal((false, false), shrunk);
    }

    [Fact]
    public void A_table_that_grows_wider_than_its_area_starts_hiding_its_right_side()
    {
        var said = ScriptFixture.Run("ScrollEdgesFixture");

        var grown = Edges(said, "grownWider");

        Assert.Equal((false, true), grown);
    }

    [Fact]
    public void A_right_to_left_area_says_its_physical_sides_as_the_sticky_cells_hold_them()
    {
        var said = ScriptFixture.Run("ScrollEdgesFixture");

        var (start, middle, end, fits) = (Edges(said, "rtlAtStart"), Edges(said, "rtlInTheMiddle"), Edges(said, "rtlAtTheEnd"), Edges(said, "rtlFits"));

        // A right-to-left area starts at its RIGHT edge: what it hides lies past its left one.
        Assert.Equal((true, false), start);
        Assert.Equal((true, true), middle);
        Assert.Equal((false, true), end);
        Assert.Equal((false, false), fits);
    }

    // [hidden past the left edge, hidden past the right edge].
    private static (bool Left, bool Right) Edges(JsonElement said, string name)
    {
        var pair = said.GetProperty(name);
        return (pair[0].GetBoolean(), pair[1].GetBoolean());
    }
}
