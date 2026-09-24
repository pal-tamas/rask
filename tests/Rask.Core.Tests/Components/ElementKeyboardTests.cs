using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

// OnKeyDown / OnKeyUp on Element: focus-scoped keyboard events wired through data-rask-on-keydown /
// data-rask-on-keyup, dispatched into a typed KeyboardEventArgs. Each ships a sync
// Action<KeyboardEventArgs> and an async Func<KeyboardEventArgs, Task> sibling (OnKeyDownAsync /
// OnKeyUpAsync), the same typed-pair convention as OnClick/OnClickAsync.
public partial class ElementKeyboardTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Key_handlers_outside_live_context_not_emitted() =>
        // No LiveRenderContext (plain ToHtml): handlers can't register, so nothing is emitted.
        Assert.Equal(
            "<div></div>",
            Div
                .OnKeyDown(_ => { })
                .OnKeyUp(_ => { }).ToHtml());

    [Fact]
    public void Key_handlers_only_non_null_emitted()
    {
        var view = new StubComponent(() => Div.OnKeyDown(_ => { }));
        Assert.Equal("<div data-rask-on-keydown=\"h0\"></div>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void Key_handlers_async_siblings_emit()
    {
        // Setting only the async variant still registers the handler and emits the attribute.
        var view = new StubComponent(() => Div
            .OnKeyDown(_ => Task.CompletedTask)
            .OnKeyUp(_ => Task.CompletedTask));
        Assert.Equal(
            "<div data-rask-on-keydown=\"h0\" data-rask-on-keyup=\"h1\"></div>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Key_handlers_emit_after_drag_hooks_before_accessibility_attrs()
    {
        var view = new StubComponent(() => Div
            .Id("d")
            .Class("x")
            .Draggable(true)
            .OnDragStart(() => { })
            .OnKeyDown(_ => { })
            .OnKeyUp(_ => { })
            .Role("dialog")
            .TabIndex(-1));

        // Documented universal order: id, class, style, data-* (incl. event hooks: drag then
        // keyboard), role, tabindex, aria-*. Action ids follow registration order:
        // dragstart=h0, keydown=h1, keyup=h2.
        Assert.Equal(
            "<div id=\"d\" class=\"x\" draggable=\"true\" " +
            "data-rask-on-dragstart=\"h0\" data-rask-on-keydown=\"h1\" data-rask-on-keyup=\"h2\" " +
            "role=\"dialog\" tabindex=\"-1\"></div>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Unset_key_handlers_add_no_footprint()
    {
        // Hoisted into the lazy LiveState: a plain element keeps the key handlers null and never
        // forces a LiveState allocation just by leaving them unset (the allocation-pin tests guard
        // the per-render cost; this asserts the property contract directly).
        var div = Div;
        Assert.Null(div.OnKeyDown);
        Assert.Null(div.OnKeyUp);
    }

    [Fact]
    public async Task Key_down_typed_handler_receives_parsed_key_code_modifiers_and_repeat()
    {
        KeyboardEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnKeyDown(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-keydown")!;

        using var payload = JsonDocument.Parse(
            "{\"key\":\"Escape\",\"code\":\"Escape\",\"shiftKey\":true,\"ctrlKey\":false," +
            "\"altKey\":false,\"metaKey\":true,\"repeat\":true}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal("Escape", seen!.Key);
        Assert.Equal("Escape", seen.Code);
        Assert.True(seen.Shift);
        Assert.False(seen.Ctrl);
        Assert.True(seen.Meta);
        Assert.True(seen.Repeat);
    }

    [Fact]
    public async Task Key_up_async_typed_handler_is_awaited()
    {
        string? seenKey = null;
        var view = new StubComponent(() => Div
            .OnKeyUp(e =>
        {
            seenKey = e.Key;
            return Task.CompletedTask;
        }));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-keyup")!;

        using var payload = JsonDocument.Parse("{\"key\":\"a\",\"code\":\"KeyA\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.Equal("a", seenKey);
    }

    [Fact]
    public async Task Key_down_async_typed_handler_is_awaited()
    {
        KeyboardEventArgs? seen = null;
        var view = new StubComponent(() => Div
            .OnKeyDown(e =>
        {
            seen = e;
            return Task.CompletedTask;
        }));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-keydown")!;

        using var payload = JsonDocument.Parse("{\"key\":\"Enter\",\"code\":\"Enter\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal("Enter", seen!.Key);
    }

    [Fact]
    public async Task Key_down_missing_payload_fields_default_to_empty_and_false()
    {
        KeyboardEventArgs? seen = null;
        var view = new StubComponent(() => Div.OnKeyDown(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-keydown")!;

        using var payload = JsonDocument.Parse("{}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);

        Assert.NotNull(seen);
        Assert.Equal("", seen!.Key);
        Assert.Equal("", seen.Code);
        Assert.False(seen.Shift);
        Assert.False(seen.Repeat);
    }
}
