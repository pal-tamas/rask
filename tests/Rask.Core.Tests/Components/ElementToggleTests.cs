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
    public void ToggleHandlers_OutsideLiveContext_NotEmitted() =>
        // No LiveRenderContext (plain ToHtml): handlers can't register, so nothing is emitted.
        Assert.Equal(
            "<div></div>",
            Div
                .OnToggle(_ => { })
                .OnBeforeToggle(_ => { }).ToHtml());

    [Fact]
    public void ToggleHandlers_OnlyNonNullEmitted()
    {
        var view = new StubComponent(() => Div.OnToggle(_ => { }));
        Assert.Equal("<div data-rask-on-toggle=\"h0\"></div>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void ToggleHandlers_AsyncSiblingsEmit()
    {
        var view = new StubComponent(() => Div
            .OnToggleAsync(_ => Task.CompletedTask)
            .OnBeforeToggleAsync(_ => Task.CompletedTask));
        // beforetoggle leads: GlobalEventOrder is chronological within a group, as drag and keyboard
        // are. The ids follow it too — RegisterHandler is called during EMISSION, not when the handler
        // was wired, so h0 goes to whichever attribute is written first.
        Assert.Equal(
            "<div data-rask-on-beforetoggle=\"h0\" data-rask-on-toggle=\"h1\"></div>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void UnsetToggleHandlers_AddNoFootprint()
    {
        var div = Div.Value;
        Assert.Null(div.OnToggle);
        Assert.Null(div.OnToggleAsync);
        Assert.Null(div.OnBeforeToggle);
        Assert.Null(div.OnBeforeToggleAsync);
    }

    [Fact]
    public async Task Toggle_TypedHandler_ReceivesThePlatformsOwnStates()
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
    public async Task Toggle_ClosingIsNotOpen()
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
    public async Task BeforeToggle_AsyncTypedHandler_IsAwaited()
    {
        string? seenState = null;
        var view = new StubComponent(() => Div.OnBeforeToggleAsync(e =>
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
    public async Task Toggle_MissingStates_DoNotThrow()
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
    public async Task AToggleFrame_DoesNotFeedAKeyboardHandler()
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
