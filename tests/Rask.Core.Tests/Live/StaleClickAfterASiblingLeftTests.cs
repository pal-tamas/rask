using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// A KNOWN DEFECT, pinned as it is today — not the behaviour wanted. A handler id is the handler's slot in its
// component's walk order, handed out again on every render. When the Cancel button leaves, Delete is given
// Cancel's id; a click on Cancel that was already on its way arrives with that id and runs Delete.
//
// It is decided on the server when the event is dispatched, so the shape of the reply that took Cancel away
// makes no difference: the whole page, a keyed diff and a diff of one run all leave the same id behind. The
// day ids stop being reused across renders this test fails, which is the point of it.
public partial class StaleClickAfterASiblingLeftTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData("the whole page")]
    [InlineData("a keyed diff")]
    [InlineData("a diff of one run")]
    public async Task A_click_sent_to_a_button_that_has_since_left_runs_the_handler_that_was_given_its_id(string reply)
    {
        var shown = true;
        var cancelled = 0;
        var deleted = 0;
        Component Cancel() => Button.OnClick(() => cancelled++)["Cancel"];
        Component Delete() => Button.OnClick(() => deleted++)["Delete"];
        var page = new StubComponent(() => reply switch
        {
            "the whole page" => [shown ? Cancel() : null, Delete()],
            "a keyed diff" => Div[
                shown ? Button.Key("c").OnClick(() => cancelled++)["Cancel"] : null,
                Button.Key("d").OnClick(() => deleted++)["Delete"]
            ],
            _ => Div[shown ? Cancel() : null, Delete()],
        });
        using var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, page, ops, out _);
        shown = false;
        Render(cache, page, ops, out var usedKeyedPath);

        var ran = await page.TryInvokeHandlerAsync("h0", JsonDocument.Parse("""{"id":"h0","type":"click"}""").RootElement);

        Assert.Equal(reply != "the whole page", LiveDiffGate.DiffOpsAreClientSupported(ops));
        Assert.Equal(reply == "a keyed diff", usedKeyedPath);
        Assert.True(ran);
        Assert.Equal(0, cancelled);
        Assert.Equal(1, deleted);
    }

    private static void Render(SessionRenderCache cache, Component page, List<EditOp> ops, out bool usedKeyedPath)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = page.RenderAsLiveRoot();
        }

        cache.TryComputeDiff(ops, out usedKeyedPath, html);
    }
}
