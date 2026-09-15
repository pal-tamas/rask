using Rask.TestSupport;

namespace Rask.DevTools.Tests.Host;

/// <summary>
///     The page's side of the Errors tab, driven in Node against a stub DOM: the button the devtools add to the runtime's
///     dev-error overlay, and a request to show the errors that waits for the panel.
/// </summary>
public sealed class HostErrorsTests
{
    [Fact]
    public void The_overlay_gains_an_open_in_devtools_button_and_a_show_request_waits_for_the_panel()
    {
        // No node on PATH — the JS-driven check cannot run. Deliberately not a failure: node is not required to build
        // or test Rask, and the devtools' browser check drives the same code for real.
        var result = NodeFixture.Run("HostErrorsFixture");
        if (result is null)
        {
            return;
        }

        var r = result.Value;
        bool Bool(string name) => r.GetProperty(name).GetBoolean();
        string? Str(string name) => r.GetProperty(name).GetString();

        // Added once, first among the overlay's buttons and styled as they are; an overlay without its bar is left alone.
        Assert.True(Bool("added"));
        Assert.False(Bool("addedAgain"));
        Assert.Equal("rask-deverr__kind,Open in DevTools,Stack,Dismiss", Str("barOrder"));
        Assert.Equal("rask-deverr__btn", Str("buttonClass"));
        Assert.Equal(1, r.GetProperty("opened").GetInt32());
        Assert.False(Bool("noBar"));

        // The runtime adds its overlay later: it gets the button when it appears.
        Assert.True(Bool("decoratedWhenAdded"));

        // Show opens the drawer at once, but the request goes to the panel only once it is listening, and only once.
        Assert.True(Bool("openedByShow"));
        Assert.Equal(0, r.GetProperty("postsBeforePanel").GetInt32());
        Assert.Equal("show-errors", Str("postsAfterPanel"));
        Assert.Equal("show-errors,show-errors", Str("postsAtOnce"));
        Assert.True(Bool("stillOpen"));
    }
}
