using Rask.TestSupport;

namespace Rask.DevTools.Tests.Host;

/// <summary>
///     The WASM panel's frame client, driven in Node against a stub DOM: a click and a change inside the panel reach the
///     panel's session.
/// </summary>
public sealed class FrameClientTests
{
    [Fact]
    public void A_click_and_a_change_in_the_panel_are_posted_to_the_page_as_events()
    {
        // No node on PATH — the JS-driven check cannot run. Deliberately not a failure: node is not required to build
        // or test Rask, and the devtools' browser check drives the same frame for real.
        var result = NodeFixture.Run("FrameClientFixture");
        if (result is null)
        {
            return;
        }

        var r = result.Value;
        Assert.True(r.GetProperty("readyPosted").GetBoolean());
        Assert.Equal(
            "{\"id\":\"h1\",\"type\":\"click\",\"shiftKey\":false,\"ctrlKey\":true,\"altKey\":false,\"metaKey\":false}",
            r.GetProperty("click").GetString());
        Assert.True(r.GetProperty("clickPrevented").GetBoolean());
        Assert.True(r.GetProperty("plainClickIgnored").GetBoolean());
        Assert.Equal("{\"id\":\"h2\",\"type\":\"change\",\"value\":\"true\"}", r.GetProperty("change").GetString());
    }
}
