using System.Text.Json;
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

        // Show opens the drawer at once, but the request goes to the panel only once it is listening, and only once — after
        // the page failures that were waiting for it.
        Assert.True(Bool("openedByShow"));
        Assert.Equal(0, r.GetProperty("postsBeforePanel").GetInt32());
        Assert.Equal("page-error,page-error,page-error,page-error,show-errors", Str("postsAfterPanel"));
        Assert.Equal("page-error,show-errors", Str("postsAtOnce"));
        Assert.True(Bool("stillOpen"));

        // Before the panel: every failure counted on the pill at once.
        Assert.Equal("1,2,3,4", Str("alertsBeforePanel"));

        // What is handed over: an uncaught error with its own stack; a rejection with a plain value; a cross-origin
        // script's error, which has only where it came from; an island, named, with its phase (detached here, so no place).
        var reports = r.GetProperty("reports").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        Assert.Equal(4, reports.Length);
        Assert.Equal("page|TypeError|x is undefined|TypeError: x is undefined\n    at app.js:3|null|null|null", reports[0]);
        Assert.Equal("page|Unhandled rejection|nope|null|null|null|null", reports[1]);
        Assert.Equal("page|Error|Script error.|https://cdn.example/x.js:1:2|null|null|null", reports[2]);
        Assert.StartsWith("island|RangeError|bad range|RangeError: bad range", reports[3]);
        Assert.EndsWith("|Chart|update|null", reports[3]);
        Assert.True(Bool("reportTimesAreSet"));

        // Handed over: the pill counts what the panel counts, and what the page still holds, which is nothing now.
        Assert.Equal(0, r.GetProperty("alertAfterHandOver").GetInt32());
        Assert.Equal(3, r.GetProperty("alertWithPanelCount").GetInt32());
        Assert.Equal("later", Str("postedOnceListening"));
        Assert.Equal(3, r.GetProperty("alertUnchangedWhileListening").GetInt32());

        // The buffer keeps the newest; the pill counts what it holds.
        Assert.Equal(50, r.GetProperty("bufferAlert").GetInt32());
        Assert.Equal("50:e5:e54", Str("bufferHanded"));

        // An island's place counts slots as the diff does (managed nodes and formatting text under <html> are not
        // slots); a detached element has none.
        Assert.Equal("0.1.0|1|1", Str("islandPlace"));
        Assert.Equal(JsonValueKind.Null, r.GetProperty("detachedPlace").ValueKind);
    }
}
