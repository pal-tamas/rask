using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// A handler id is the handler's slot in its component's walk order, so when the Cancel button leaves, Delete is
// given Cancel's id. A click on Cancel that was already on its way arrives with that id — and with the number
// of the page it was read from, which is how the server knows the id meant Cancel: it runs nothing. A click on
// Delete sent from that same page carries the id Delete had then, and still deletes.
//
// It is decided on the server when the event is dispatched, so the shape of the reply that took Cancel away
// makes no difference: the whole page, a keyed diff and a diff of one run all leave the same id behind. This
// pinned the old answer — the click ran Delete — as a known defect until ids stopped meaning another handler.
public partial class StaleClickAfterASiblingLeftTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData("the whole page")]
    [InlineData("a keyed diff")]
    [InlineData("a diff of one run")]
    public async Task A_click_sent_to_a_button_that_has_since_left_runs_nothing_and_one_sent_to_its_neighbour_still_runs_it(string reply)
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

        var cancelRan = await page.TryInvokeHandlerAsync("h0", JsonDocument.Parse("""{"id":"h0","type":"click","v":0}""").RootElement);
        var afterCancel = (cancelled, deleted);
        var deleteRan = await page.TryInvokeHandlerAsync("h1", JsonDocument.Parse("""{"id":"h1","type":"click","v":0}""").RootElement);

        Assert.Equal(reply != "the whole page", LiveDiffGate.DiffOpsAreClientSupported(ops));
        Assert.Equal(reply == "a keyed diff", usedKeyedPath);
        Assert.False(cancelRan);
        Assert.Equal((0, 0), afterCancel);
        Assert.True(deleteRan);
        Assert.Equal((0, 1), (cancelled, deleted));
    }

    // Renders, and says the browser was sent the result.
    private static void Render(SessionRenderCache cache, Component page, List<EditOp> ops, out bool usedKeyedPath)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = page.RenderAsLiveRoot();
        }

        cache.TryComputeDiff(ops, out usedKeyedPath, html);
        page.HandlersSent();
    }
}
