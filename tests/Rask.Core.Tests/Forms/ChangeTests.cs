using Rask.Values;

namespace Rask.Core.Tests.Forms;

// F-09: what an operation does to a value it may leave alone — keep it, clear it, set it.
public sealed class ChangeTests
{
    [Fact]
    public void A_change_nobody_made_keeps_the_value()
    {
        Change<string?> change = default;

        var applied = change.Applied("kept");

        Assert.Equal(ChangeKind.Keep, change.Kind);
        Assert.Equal("kept", applied);
        Assert.Equal(Change<string?>.Keep, change);
    }

    [Fact]
    public void A_clear_takes_the_value_away_and_a_set_puts_a_new_one()
    {
        var cleared = Change<string?>.Clear.Applied("old");
        var set = Change<string?>.Set("new").Applied("old");

        Assert.Null(cleared);
        Assert.Equal("new", set);
    }
}
