using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

// OnToggle / OnBeforeToggle on Element: the open-state transition of a popover or <details>,
// dispatched into a typed ToggleEventArgs.
//
// The event exists because the state belongs to the BROWSER. A [popover] closes itself on Escape and
// on a click outside, and nothing told C# about it — so a component tracking its own open flag went
// on believing the panel was open, and its aria-expanded went on saying so over a closed panel.
public partial class ElementToggleTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Toggle_handlers_outside_a_live_context_are_not_emitted() =>
        // No LiveRenderContext (plain ToHtml): handlers can't register, so nothing is emitted.
        Assert.Equal(
            "<div></div>",
            Div
                .OnToggle(_ => { })
                .OnBeforeToggle(_ => { }).ToHtml());

    [Fact]
    public void Only_the_set_toggle_handlers_are_emitted()
    {
        var view = new StubComponent(() => Div.OnToggle(_ => { }));

        Assert.Equal("<div data-rask-on-toggle=\"h0\"></div>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void Async_toggle_handlers_emit_too()
    {
        var view = new StubComponent(() => Div
            .OnToggle(_ => Task.CompletedTask)
            .OnBeforeToggle(_ => Task.CompletedTask));

        // beforetoggle leads: GlobalEventOrder is chronological within a group, as drag and keyboard
        // are. The ids follow it too — RegisterHandler is called during EMISSION, not when the handler
        // was wired, so h0 goes to whichever attribute is written first.
        Assert.Equal(
            "<div data-rask-on-beforetoggle=\"h0\" data-rask-on-toggle=\"h1\"></div>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Unset_toggle_handlers_leave_no_footprint()
    {
        var div = Div;

        Assert.Null(div.OnToggle);
        Assert.Null(div.OnBeforeToggle);
    }

    [Fact]
    public async Task A_typed_toggle_handler_receives_the_platforms_own_states()
    {
        // Passed through rather than translated to a bool: these are the words the DOM event carries,
        // so a caller comparing against "open" is comparing against the spec.
        ToggleEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnToggle(e => seen = e));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-toggle")!;

        using var payload = JsonDocument.Parse("{\"oldState\":\"closed\",\"newState\":\"open\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal("closed", seen!.OldState);
        Assert.Equal("open", seen.NewState);
        Assert.True(seen.IsOpen);
    }

    [Fact]
    public async Task A_closing_toggle_is_not_open()
    {
        // The transition this whole event exists for: the browser dismissed the popover and C# has to
        // hear about it, or aria-expanded goes on claiming the panel is open.
        ToggleEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnToggle(e => seen = e));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-toggle")!;

        using var payload = JsonDocument.Parse("{\"oldState\":\"open\",\"newState\":\"closed\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.False(seen!.IsOpen);
    }

    [Fact]
    public async Task An_async_typed_beforetoggle_handler_is_awaited()
    {
        string? seenState = null;
        var view = new StubComponent(() => Div.OnBeforeToggle(e =>
        {
            seenState = e.NewState;
            return Task.CompletedTask;
        }));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-beforetoggle")!;

        using var payload = JsonDocument.Parse("{\"oldState\":\"closed\",\"newState\":\"open\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.Equal("open", seenState);
    }

    [Fact]
    public async Task Missing_toggle_states_do_not_throw()
    {
        // A client from another deploy, or a host that tags frames sparsely. An absent state reads as
        // empty rather than as an exception on the dispatch path.
        ToggleEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnToggle(e => seen = e));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-toggle")!;

        using var payload = JsonDocument.Parse("{}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.Equal("", seen!.OldState);
        Assert.False(seen.IsOpen);
    }

    [Fact]
    public async Task A_toggle_frame_does_not_feed_a_keyboard_handler()
    {
        // HandlerFrameShape's whole job: a frame that outlived the render it was issued against must
        // not run whatever now sits in that slot. Toggle and Keyboard are different shapes, so the
        // mismatch is refused rather than fed an empty KeyboardEventArgs.
        var ran = false;
        var view = new StubComponent(() => Div.OnKeyDown(_ => ran = true));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-keydown")!;

        using var payload = JsonDocument.Parse(
            "{\"type\":\"toggle\",\"oldState\":\"closed\",\"newState\":\"open\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.False(ran);
    }
}
