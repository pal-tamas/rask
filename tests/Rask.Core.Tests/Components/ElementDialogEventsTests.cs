using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

// OnCancel / OnClose on Element: a <dialog>'s two endings (#1116).
//
// close fires for every way a dialog closes; cancel only for a DISMISSAL — Escape, or a light dismiss —
// and before close. Without cancel a component could not tell "the user backed out" from "the user
// finished", which is the difference between discarding a draft and keeping it.
public partial class ElementDialogEventsTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Dialog_handlers_outside_live_context_not_emitted() =>
        Assert.Equal("<dialog></dialog>", Dialog.OnCancel(() => { }).OnClose(() => { }).ToHtml());

    [Fact]
    public void Dialog_handlers_only_non_null_emitted()
    {
        var view = new StubComponent(() => Dialog.OnClose(() => { }));
        Assert.Equal("<dialog data-rask-on-close=\"h0\"></dialog>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void Cancel_comes_first_among_the_dialog_handlers_because_it_fires_first()
    {
        // Appended after toggle, so no earlier attribute moves; cancel before close, as the browser
        // raises them. The ids follow the emit order, not the order the handlers were wired in.
        var view = new StubComponent(() => Dialog
            .OnClose(() => Task.CompletedTask)
            .OnToggle(_ => { })
            .OnCancel(() => Task.CompletedTask));
        Assert.Equal(
            "<dialog data-rask-on-toggle=\"h0\" data-rask-on-cancel=\"h1\" data-rask-on-close=\"h2\"></dialog>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Unset_dialog_handlers_add_no_footprint()
    {
        var dialog = Dialog;
        Assert.False(dialog.OnCancel.HasValue);
        Assert.False(dialog.OnClose.HasValue);
    }

    [Fact]
    public async Task A_cancel_frame_reaches_the_cancel_handler()
    {
        var cancelled = 0;
        var view = new StubComponent(() => Dialog.OnCancel(() => cancelled++));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-cancel")!;

        using var payload = JsonDocument.Parse("{\"type\":\"cancel\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.Equal(1, cancelled);
    }

    [Fact]
    public async Task A_close_frame_reaches_an_async_close_handler()
    {
        var closed = 0;
        var view = new StubComponent(() => Dialog.OnClose(async () =>
        {
            await Task.Yield();
            closed++;
        }));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-close")!;

        using var payload = JsonDocument.Parse("{\"type\":\"close\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.Equal(1, closed);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("click")]
    [InlineData("reset")]
    public void Both_feed_a_parameterless_handler_through_the_one_list(string eventName) =>
        // The list Rask.Blazor reads too, rather than a copy of it.
        Assert.True(HandlerFrameShape.FeedsParameterless(eventName));

    [Theory]
    [InlineData("toggle")]
    [InlineData("input")]
    [InlineData("Cancel")]
    public void Nothing_else_feeds_a_parameterless_handler(string eventName) =>
        Assert.False(HandlerFrameShape.FeedsParameterless(eventName));
}
