using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

// The events every element fires, generated from MDN's GlobalEventHandlers: wired through data-rask-on-{event}
// and dispatched into MDN's event types (MouseEvent, PointerEvent, TouchEvent, …), inheritance included.
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
            "<span data-rask-on-contextmenu=\"h0\" data-rask-on-mouseenter=\"h1\"></span>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Handlers_emit_in_mdns_order_whatever_order_they_were_wired_in()
    {
        var view = new StubComponent(() => Div
            .OnScroll(_ => { })
            .OnFocus(() => { })
            .OnMouseDown(_ => { })
            .OnClick(() => { }));
        // GlobalEventHandlers' order in MDN's IDL: click, focus, mousedown, scroll (ids follow emit order).
        Assert.Equal(
            "<div data-rask-on-click=\"h0\" data-rask-on-focus=\"h1\" " +
            "data-rask-on-mousedown=\"h2\" data-rask-on-scroll=\"h3\"></div>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Unset_handlers_add_no_footprint()
    {
        var div = Div;
        Assert.False(div.OnClick.HasValue);
        Assert.False(div.OnMouseMove.HasValue);
        Assert.False(div.OnPointerDown.HasValue);
        Assert.False(div.OnFocus.HasValue);
        Assert.False(div.OnWheel.HasValue);
    }

    [Fact]
    public async Task Mouse_typed_handler_receives_geometry_buttons_and_modifiers()
    {
        MouseEvent? seen = null;
        var view = new StubComponent(() => Div.OnMouseDown(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-mousedown")!;

        using var payload = JsonDocument.Parse(
            "{\"button\":2,\"buttons\":2,\"clientX\":12,\"clientY\":24,\"pageX\":12.5,\"pageY\":99," +
            "\"offsetX\":3,\"offsetY\":4,\"movementX\":-1,\"movementY\":2,\"shiftKey\":true,\"metaKey\":true}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal(2, seen!.Button);
        Assert.Equal(2, seen.Buttons);
        Assert.Equal(12, seen.ClientX);
        Assert.Equal(12.5, seen.PageX);
        Assert.Equal(24, seen.ClientY);
        Assert.Equal(99, seen.PageY);
        Assert.True(seen.ShiftKey);
        Assert.True(seen.MetaKey);
        Assert.False(seen.CtrlKey);
    }

    [Fact]
    public async Task A_handler_typed_to_a_base_event_receives_the_derived_one()
    {
        MouseEvent? seen = null;
        Action<MouseEvent> onMouse = e => seen = e;
        var view = new StubComponent(() => Div.OnClick(onMouse));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-click")!;

        using var payload = JsonDocument.Parse("{\"type\":\"click\",\"clientX\":7,\"pointerType\":\"touch\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        var pointer = Assert.IsType<PointerEvent>(seen);
        Assert.Equal(7, pointer.ClientX);
        Assert.Equal("touch", pointer.PointerType);
    }

    [Fact]
    public async Task A_components_argument_less_callback_forwards_into_an_element_event()
    {
        var fired = 0;
        var forwarded = new Callback(() => fired++);
        var view = new StubComponent(() => Button.OnClick(forwarded)["x"]);
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-click")!;

        using var payload = JsonDocument.Parse("{\"clientX\":1}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Wheel_typed_handler_receives_deltas_and_the_mouse_fields_it_inherits()
    {
        WheelEvent? seen = null;
        var view = new StubComponent(() => Div.OnWheel(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-wheel")!;

        using var payload = JsonDocument.Parse(
            "{\"deltaX\":0,\"deltaY\":120,\"deltaZ\":0,\"deltaMode\":1,\"clientX\":5,\"ctrlKey\":true}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal(120, seen!.DeltaY);
        Assert.Equal(1, seen.DeltaMode);
        Assert.Equal(5, seen.ClientX);
        Assert.True(seen.CtrlKey);
    }

    [Fact]
    public async Task Pointer_typed_handler_receives_pointer_fields_and_the_mouse_fields_it_inherits()
    {
        PointerEvent? seen = null;
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
        Assert.Equal(3, seen.ClientX);
    }

    [Fact]
    public async Task Touch_typed_handler_receives_every_touch_in_mdns_shape()
    {
        TouchEvent? seen = null;
        var view = new StubComponent(() => Div.OnTouchStart(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-touchstart")!;

        using var payload = JsonDocument.Parse(
            "{\"touches\":[{\"identifier\":1,\"clientX\":100,\"clientY\":200},{\"identifier\":2,\"clientX\":5}]," +
            "\"altKey\":true}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal(2, seen!.Touches.Count);
        Assert.Equal(100, seen.Touches[0].ClientX);
        Assert.Equal(200, seen.Touches[0].ClientY);
        Assert.Equal(2, seen.Touches[1].Identifier);
        Assert.Empty(seen.ChangedTouches);
        Assert.True(seen.AltKey);
    }

    [Fact]
    public async Task Clipboard_typed_handler_reads_the_pasted_text_from_its_clipboard_data()
    {
        ClipboardEvent? seen = null;
        var view = new StubComponent(() => Div.OnPaste(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-paste")!;

        using var payload = JsonDocument.Parse(
            "{\"clipboardData\":{\"types\":[\"text/plain\"],\"data\":{\"text/plain\":\"hello world\"}}}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen?.ClipboardData);
        Assert.Equal("hello world", seen!.ClipboardData!.GetData("text/plain"));
        Assert.Equal(["text/plain"], seen.ClipboardData.Types);
        Assert.Equal("", seen.ClipboardData.GetData("text/html"));
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
    public async Task Before_input_typed_handler_receives_the_inserted_text()
    {
        InputEvent? seen = null;
        var view = new StubComponent(() => Div.OnBeforeInput(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-beforeinput")!;

        using var payload = JsonDocument.Parse("{\"data\":\"x\",\"inputType\":\"insertText\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.Equal("x", seen?.Data);
        Assert.Equal("insertText", seen?.InputType);
    }

    [Fact]
    public async Task Media_handler_on_audio_reads_playback_state_from_its_target()
    {
        Event? seen = null;
        var view = new StubComponent(() => Audio.OnTimeUpdate(e => seen = e));
        var html = view.RenderAsLiveRoot();
        Assert.Contains("data-rask-on-timeupdate=\"h0\"", html);

        using var payload = JsonDocument.Parse(
            "{\"target\":{\"currentTime\":12.3,\"duration\":60,\"paused\":false,\"ended\":false," +
            "\"volume\":0.8,\"muted\":false,\"playbackRate\":1.5}}");
        await view.TryInvokeHandlerAsync("h0", payload.RootElement);

        var media = seen?.Target;
        Assert.NotNull(media);
        Assert.Equal(12.3, media!.CurrentTime);
        Assert.Equal(60, media.Duration);
        Assert.False(media.Paused);
        Assert.Equal(0.8, media.Volume);
        Assert.Equal(1.5, media.PlaybackRate);
    }

    [Fact]
    public void Media_events_emit_with_every_other_event_before_the_media_attributes()
    {
        var view = new StubComponent(() => Video
            .Src("/v.mp4")
            .Controls(true)
            .OnPlay(_ => { })
            .OnPause(_ => { }));
        Assert.Equal(
            "<video data-rask-on-pause=\"h0\" data-rask-on-play=\"h1\" src=\"/v.mp4\" controls></video>",
            view.RenderAsLiveRoot());
    }
}
