using System.Text.Json;

namespace Rask.Core.Tests.Live;

// The runtime's tablist keyboard: the arrow keys move to the next tab that is not disabled, around the ends.
//
// It is a behaviour hook (rask-tabs.ts) because C# can neither move focus nor keep an arrow key from scrolling the page.
// The rule — which tab — is a pure function, driven here in a Node subprocess; that the tab is then focused
// and pressed is the site's E2E (UiKitNavigationTests).
public sealed class TablistKeyboardTests
{
    [Fact]
    public void An_arrow_key_moves_one_tab_and_wraps_at_the_ends()
    {
        // No node on PATH — deliberately not a failure: node is not required to build or test Rask.
        if (NodeFixture.Run("TablistKeyboardFixture") is not { } moves)
        {
            return;
        }

        var (next, previous) = (Landed(moves, "next"), Landed(moves, "previous"));
        var (forward, back) = (Landed(moves, "wrapsForward"), Landed(moves, "wrapsBack"));

        Assert.Equal(1, next);
        Assert.Equal(1, previous);
        Assert.Equal(0, forward);
        Assert.Equal(2, back);
    }

    [Fact]
    public void An_arrow_key_passes_over_disabled_tabs()
    {
        if (NodeFixture.Run("TablistKeyboardFixture") is not { } moves)
        {
            return;
        }

        var (forward, back) = (Landed(moves, "skipsForward"), Landed(moves, "skipsBack"));
        var (run, runBack, end) = (Landed(moves, "skipsARun"), Landed(moves, "skipsARunBack"), Landed(moves, "skipsAtTheEnd"));

        Assert.Equal(2, forward);
        Assert.Equal(0, back);
        Assert.Equal(4, run);
        Assert.Equal(1, runBack);
        Assert.Equal(0, end);
    }

    [Fact]
    public void An_arrow_key_with_no_tab_to_start_from_enters_at_the_near_end()
    {
        if (NodeFixture.Run("TablistKeyboardFixture") is not { } moves)
        {
            return;
        }

        var (forward, back) = (Landed(moves, "fromNowhereForward"), Landed(moves, "fromNowhereBack"));

        Assert.Equal(0, forward);
        Assert.Equal(2, back);
    }

    [Fact]
    public void An_arrow_key_goes_nowhere_when_no_other_tab_can_be_reached()
    {
        if (NodeFixture.Run("TablistKeyboardFixture") is not { } moves)
        {
            return;
        }

        var (alone, none, empty) = (Landed(moves, "onlyOneLeft"), Landed(moves, "noneLeft"), Landed(moves, "empty"));

        Assert.Equal(1, alone);
        Assert.Equal(-1, none);
        Assert.Equal(-1, empty);
    }

    private static int Landed(JsonElement moves, string move) => moves.GetProperty(move).GetInt32();
}
