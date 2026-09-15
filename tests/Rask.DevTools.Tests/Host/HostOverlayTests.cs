using Rask.TestSupport;

namespace Rask.DevTools.Tests.Host;

/// <summary>
///     The page overlays' logic, driven in Node against stub DOMs: reading and matching places, what the page does with the
///     panel frame's messages and which it ignores, measuring a box from the page's nodes, and what the panel side posts.
/// </summary>
public sealed class HostOverlayTests
{
    [Fact]
    public void Places_match_messages_are_filtered_boxes_are_measured_and_the_panel_posts_what_it_is_over()
    {
        // No node on PATH — the JS-driven check cannot run. Deliberately not a failure: node is not required to build
        // or test Rask, and the devtools' browser check drives the same code for real.
        var result = NodeFixture.Run("HostOverlayFixture");
        if (result is null)
        {
            return;
        }

        var r = result.Value;
        bool Bool(string name) => r.GetProperty(name).GetBoolean();
        string? Str(string name) => r.GetProperty(name).GetString();

        // Places: the three parts, the top level, and nothing malformed ever becomes a place.
        Assert.Equal("{\"path\":[1,0,2],\"first\":3,\"count\":2}", Str("placeParsed"));
        Assert.Equal("{\"path\":[],\"first\":0,\"count\":1}", Str("topPlaceParsed"));
        Assert.True(Bool("malformedRejected"));
        Assert.Equal(2, r.GetProperty("anchorsKeptOnlyValid").GetInt32());
        Assert.Equal(0, r.GetProperty("anchorsOfGarbage").GetInt32());

        // A place holds its own slots and everything inside them, and nothing beside or above.
        Assert.True(Bool("containsFirst"));
        Assert.True(Bool("containsInsideLast"));
        Assert.True(Bool("containsNotAfter"));
        Assert.True(Bool("containsNotParent"));
        Assert.True(Bool("containsNotSibling"));

        // The deepest place wins, and between equals the one further down the tree.
        Assert.Equal("row", Str("deepestWins"));
        Assert.Equal("wrapper-of-inner", Str("tieGoesToLater"));
        Assert.True(Bool("outsideIsNull"));

        // The bridge: a hover shows and clears, a pick starts and its choice goes back, the shortcut toggles, and closing
        // the drawer mid-pick tells the panel — but not when nothing was being picked.
        Assert.Equal("show 1|0|1 Card | hide | pick 7 | stop | hide | stop | stop | hide", Str("bridgeCalls"));
        Assert.True(Bool("pickedPosted"));
        Assert.Equal(1, r.GetProperty("toggles").GetInt32());
        Assert.True(Bool("unknownKindNotHandled"));
        Assert.True(Bool("closeWhilePickingCancelled"));
        Assert.True(Bool("closeWhileIdlePostsNothing"));

        // Only the frame's own window, in the named origin, on our channel; what the bridge leaves goes to the host.
        Assert.Equal("highlight,event,other:event", Str("heard"));

        // Boxes: two siblings unioned through the runtime's own path walk (a managed node and a comment not counted);
        // a zero-size node draws nothing; a place that no longer resolves draws nothing.
        Assert.Equal("10,20,230,50", Str("union"));
        Assert.True(Bool("zeroSizeSkipped"));
        Assert.True(Bool("unresolvedIsNull"));
        Assert.Equal("[1,1,1]", Str("pathOfSection"));
        Assert.True(Bool("pathOfManagedIsNull"));
        Assert.Equal("TaskRow · 612×44", Str("label"));

        // The panel side: one post per row entered, cleared outside a row and when the pointer leaves the panel; a pick
        // posted when the anchors appear, change and go; a pick reported back only from the page, as a keydown.
        Assert.Equal("1|0|1:Card,null:null", Str("hoverPosts"));
        Assert.Equal("pick:[],pick:null", Str("pickPosts"));
        Assert.Equal("pick:42/true,pick:cancel/true", Str("pickedKeys"));

        // Flashing, at the bridge: the setting is remembered in the page's storage and forgotten again, and only
        // well-formed boxes are flashed.
        Assert.Equal("on", Str("flashRemembered"));
        Assert.True(Bool("flashForgotten"));
        Assert.Equal("enabled true | renders 1|0|1=Row · state | enabled false", Str("flashCalls"));

        // Flashing, from the panel: the remembered setting reported once as a keydown; the page told of the panel's
        // setting; commits already held when it came on are not flashed, each newer commit is flashed once, and one with
        // no places posts nothing.
        Assert.Equal("flash:on", Str("flashReported"));
        Assert.Equal(0, r.GetProperty("flashPostsBeforeAgreement").GetInt32());
        Assert.Equal("setting:true,flash:C · props,setting:false", Str("flashPosts"));
        Assert.True(Bool("flashReportedOnce"));
        Assert.Equal(0, r.GetProperty("flashesOfGarbage").GetInt32());
        Assert.Equal("[[2,[[\"a\",\"b\"]]]]", Str("flashesKeepValidBoxes"));

        // The DOM flash boxes what changed: the item (text, attribute and insertion, once), the list (text added to it) —
        // not the body or document toggling an attribute, the devtools' own element, or anything in <head>.
        Assert.Equal("li,ul", Str("domChanged"));

        // Patch times: a burst from the page is one report once the batch interval passes; a report from anywhere else,
        // or one that is not a finite non-negative number, is dropped. The page's hook posts a frame's apply time.
        Assert.Equal(1, r.GetProperty("patchTimersScheduled").GetInt32());
        Assert.Equal("patch:3.46@2048,12.00@-1", Str("patchKeys"));

        // Errors: the bridge puts the panel's count on the pill (ignoring a count that is not a number) and notes that
        // the panel is listening; the panel posts its count when it changes, and hands the page's show request over once.
        Assert.True(Bool("errorCountHandled"));
        Assert.Equal("4", Str("alerts"));
        Assert.True(Bool("heardFromPanel"));
        Assert.Equal("count:2,count:0", Str("errorPosts"));
        Assert.Equal("errors:show", Str("showErrorsKeys"));
        // The browser is named first, once, for a bug report; then the failure.
        Assert.Matches("^browser:[^,]+,page-error:\\{\"kind\":\"page\",\"title\":\"TypeError\",\"message\":\"boom\"\\}$", Str("pageErrorKeys"));
        Assert.Equal("Chrome 131,Edge 131,Firefox 133,Safari 18.1,Unknown browser", Str("browsers"));
        Assert.Equal("timed@357,zero@-1", Str("patchPosted"));
    }
}
