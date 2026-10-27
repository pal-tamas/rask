namespace Rask.Core.Tests.Live;

// The whole page a connection is sent when it replaces one (#1258) repeats every field it did not change. The
// frames before it would have sent nothing for such a field, so it leaves what the reader typed or ticked there,
// though focus has moved on; a value the server did change still wins. Pairs with the Server E2E
// CredentialChangedElsewhereTests, whose tab reconnects with what was typed in an unfocused field.
public sealed class MorphCatchUpTests
{
    [Fact]
    public void A_catch_up_leaves_what_was_typed_and_ticked_where_it_repeats_the_render_and_takes_what_it_changed()
    {
        var result = NodeFixture.Run("MorphCatchUpFixture");

        // Node is not required to test Rask; the browser half is the E2E above.
        if (result is null)
        {
            return;
        }

        var root = result.Value;
        Assert.Equal("half a thought", root.GetProperty("catchUpRepeats").GetString());
        Assert.Equal("from the server", root.GetProperty("catchUpChanges").GetString());
        Assert.Equal(string.Empty, root.GetProperty("pageRepeats").GetString());
        Assert.False(root.GetProperty("catchUpRepeatsTick").GetBoolean());
        Assert.True(root.GetProperty("pageRepeatsTick").GetBoolean());
    }
}
