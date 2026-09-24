using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

// The unified GlobalEventHandlers surface on Element: mouse/pointer/touch/wheel/focus/clipboard plus the
// remaining drag/form events, available on EVERY element, wired through data-rask-on-{event} and
// dispatched into the typed *EventArgs. Mirrors ElementKeyboardTests/ElementDragTests for the new events.
public partial class ElementDomEventsTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Handlers_outside_live_context_not_emitted() =>
        Assert.Equal("<div></div>", Div.OnMouseDown(_ => { }).ToHtml());

    [Fact]
    public void Events_are_universal_on_every_element()
    {
        // The surface lives on Element, so a Span (no tag-specific handlers of its own) exposes them.
        var view = new StubComponent(() => Span
            .OnMouseEnter(_ => { })
            .OnContextMenu(_ => { }));
        Assert.Equal(
            "<span data-rask-on-mouseenter=\"h0\" data-rask-on-contextmenu=\"h1\"></span>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Handlers_emit_in_a_fixed_order_click_then_mouse_then_focus_then_scroll()
    {
        var view = new StubComponent(() => Div
            .OnScroll(_ => { })
            .OnFocus(() => { })
            .OnMouseDown(_ => { })
            .OnClick(() => { }));
        // GlobalEventOrder: click, …mouse…, focus, …, scroll (registration ids follow emit order).
        Assert.Equal(
            "<div data-rask-on-click=\"h0\" data-rask-on-mousedown=\"h1\" " +
            "data-rask-on-focus=\"h2\" data-rask-on-scroll=\"h3\"></div>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Unset_handlers_add_no_footprint()
    {
        var div = Div;
        Assert.Null(div.OnClick);
        Assert.Null(div.OnMouseMove);
        Assert.Null(div.OnPointerDown);
        Assert.Null(div.OnFocus);
        Assert.Null(div.OnWheel);
    }

    [Fact]
    public async Task Mouse_typed_handler_receives_geometry_buttons_and_modifiers()
    {
        MouseEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnMouseDown(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-mousedown")!;

        using var payload = JsonDocument.Parse(
            "{\"button\":2,\"buttons\":2,\"clientX\":12.5,\"clientY\":24,\"pageX\":12.5,\"pageY\":99," +
            "\"offsetX\":3,\"offsetY\":4,\"movementX\":-1,\"movementY\":2,\"shiftKey\":true,\"metaKey\":true}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal(2, seen!.Button);
        Assert.Equal(2, seen.Buttons);
        Assert.Equal(12.5, seen.ClientX);
        Assert.Equal(24, seen.ClientY);
        Assert.Equal(99, seen.PageY);
        Assert.True(seen.Shift);
        Assert.True(seen.Meta);
        Assert.False(seen.Ctrl);
    }

    [Fact]
    public async Task Wheel_typed_handler_receives_deltas_and_composed_mouse()
    {
        WheelEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnWheel(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-wheel")!;

        using var payload = JsonDocument.Parse(
            "{\"deltaX\":0,\"deltaY\":120,\"deltaZ\":0,\"deltaMode\":1,\"clientX\":5,\"ctrlKey\":true}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal(120, seen!.DeltaY);
        Assert.Equal(1, seen.DeltaMode);
        Assert.Equal(5, seen.Mouse.ClientX);
        Assert.True(seen.Mouse.Ctrl);
    }

    [Fact]
    public async Task Pointer_typed_handler_receives_pointer_fields_and_composed_mouse()
    {
        PointerEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnPointerDown(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-pointerdown")!;

        using var payload = JsonDocument.Parse(
            "{\"pointerId\":7,\"pressure\":0.5,\"pointerType\":\"pen\",\"isPrimary\":true," +
            "\"tiltX\":10,\"clientX\":3,\"clientY\":4}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal(7, seen!.PointerId);
        Assert.Equal(0.5, seen.Pressure);
        Assert.Equal("pen", seen.PointerType);
        Assert.True(seen.IsPrimary);
        Assert.Equal(10, seen.TiltX);
        Assert.Equal(3, seen.Mouse.ClientX);
    }

    [Fact]
    public async Task Touch_typed_handler_receives_count_and_first_touch_coords()
    {
        TouchEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnTouchStart(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-touchstart")!;

        using var payload = JsonDocument.Parse(
            "{\"touchCount\":2,\"clientX\":100,\"clientY\":200,\"pageX\":100,\"pageY\":250,\"altKey\":true}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal(2, seen!.TouchCount);
        Assert.Equal(100, seen.ClientX);
        Assert.Equal(200, seen.ClientY);
        Assert.True(seen.Alt);
    }

    [Fact]
    public async Task Clipboard_typed_handler_receives_text()
    {
        ClipboardEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnPaste(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-paste")!;

        using var payload = JsonDocument.Parse("{\"text\":\"hello world\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal("hello world", seen!.Text);
    }

    [Fact]
    public async Task Focus_parameterless_handler_fires()
    {
        var fired = 0;
        var view = new StubComponent(() => Div.OnFocus(() => fired++));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-focus")!;

        var ok = await view.TryInvokeHandlerAsync(id, JsonDocument.Parse("{}").RootElement);

        Assert.True(ok);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void New_drag_events_emit()
    {
        var view = new StubComponent(() => Div
            .OnDrag(() => { })
            .OnDragEnter(() => { })
            .OnDragLeave(() => { }));
        Assert.Equal(
            "<div data-rask-on-drag=\"h0\" data-rask-on-dragenter=\"h1\" data-rask-on-dragleave=\"h2\"></div>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Before_input_typed_handler_receives_inserted_text()
    {
        string? seen = null;
        var view = new StubComponent(() => Div.OnBeforeInput(s => seen = s));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-beforeinput")!;

        using var payload = JsonDocument.Parse("{\"value\":\"x\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.Equal("x", seen);
    }

    [Fact]
    public async Task Media_typed_handler_on_audio_receives_playback_state()
    {
        MediaEventArgs? seen = null;
        var view = new StubComponent(() => Audio.OnTimeUpdate(e => seen = e));
        var html = view.RenderAsLiveRoot();
        Assert.Contains("data-rask-on-timeupdate=\"h0\"", html);

        using var payload = JsonDocument.Parse(
            "{\"currentTime\":12.3,\"duration\":60,\"paused\":false,\"ended\":false," +
            "\"volume\":0.8,\"muted\":false,\"playbackRate\":1.5}");
        await view.TryInvokeHandlerAsync("h0", payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal(12.3, seen!.CurrentTime);
        Assert.Equal(60, seen.Duration);
        Assert.False(seen.Paused);
        Assert.Equal(0.8, seen.Volume);
        Assert.Equal(1.5, seen.PlaybackRate);
    }

    [Fact]
    public void Media_events_emit_after_media_attributes_on_video()
    {
        var view = new StubComponent(() => Video
            .Src("/v.mp4")
            .Controls(true)
            .OnPlay(_ => { })
            .OnPause(_ => { }));
        Assert.Equal(
            "<video src=\"/v.mp4\" controls data-rask-on-play=\"h0\" data-rask-on-pause=\"h1\"></video>",
            view.RenderAsLiveRoot());
    }
}
