using System.Text.Json;
using Rask.Core.DragAndDrop;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

public partial class DragDropTests : global::Rask.Core.RaskMarkup
{
    private static JsonElement Empty => JsonDocument.Parse("{}").RootElement;

    [Fact]
    public void Being_headless_it_emits_the_body_markup_and_no_dom_of_its_own()
    {
        var view = new StubComponent(() => DragDrop.Body(ctx => Div["x"]));
        Assert.Equal("<div>x</div>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void A_missing_body_throws()
    {
        var view = new StubComponent(() => DragDrop.Body(null!));
        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Drop_after_drag_start_fires_on_drop_with_move()
    {
        DragDropMove? captured = null;
        var view = new StubComponent(() => DragDrop
            .Body(ctx => Div[
                Div.Draggable(true).OnDragStart(ctx.DragStart("zoneA", 2))["src"],
                Div.OnDrop(ctx.Drop("zoneB", 5))["dst"]
            ])
            .OnDrop(m => captured = m));

        var html = view.RenderAsLiveRoot();
        var startId = MarkupAssert.Attr(html, "data-rask-on-dragstart");
        var dropId = MarkupAssert.Attr(html, "data-rask-on-drop");

        await view.TryInvokeHandlerAsync(startId!, Empty);
        await view.TryInvokeHandlerAsync(dropId!, Empty);

        Assert.NotNull(captured);
        Assert.Equal("zoneA", captured!.FromZone);
        Assert.Equal(2, captured.FromIndex);
        Assert.Equal("zoneB", captured.ToZone);
        Assert.Equal(5, captured.ToIndex);
    }

    [Fact]
    public async Task Drop_without_drag_start_does_not_fire()
    {
        var fired = false;
        var view = new StubComponent(() => DragDrop
            .Body(ctx => Div.OnDrop(ctx.Drop("z", 0))["dst"])
            .OnDrop(_ => fired = true));

        var dropId = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-drop");
        await view.TryInvokeHandlerAsync(dropId!, Empty);

        Assert.False(fired);
    }

    [Fact]
    public async Task Drag_over_after_drag_start_marks_drop_target()
    {
        DragDropContext? captured = null;
        var view = new StubComponent(() => DragDrop
            .Body(ctx =>
            {
                captured = ctx;
                return Div[
                    Div.Draggable(true).OnDragStart(ctx.DragStart("z", 0))["s"],
                    Div.OnDragOver(ctx.DragOver("z", 1))["o"]
                ];
            })
            .OnDrop(_ => { }));

        var html = view.RenderAsLiveRoot();
        var startId = MarkupAssert.Attr(html, "data-rask-on-dragstart");
        var overId = MarkupAssert.Attr(html, "data-rask-on-dragover");

        await view.TryInvokeHandlerAsync(startId!, Empty);
        await view.TryInvokeHandlerAsync(overId!, Empty);
        view.RenderAsLiveRoot();

        Assert.NotNull(captured);
        Assert.True(captured!.IsDragging);
        Assert.Equal("z", captured.SourceZone);
        Assert.Equal(0, captured.SourceIndex);
        Assert.Equal("z", captured.TargetZone);
        Assert.Equal(1, captured.TargetIndex);
        Assert.True(captured.IsDropTarget("z", 1));
        Assert.True(captured.IsSource("z", 0));
    }

    [Fact]
    public async Task Drag_over_without_drag_start_ignored()
    {
        DragDropContext? captured = null;
        var view = new StubComponent(() => DragDrop
            .Body(ctx =>
            {
                captured = ctx;
                return Div.OnDragOver(ctx.DragOver("z", 1))["o"];
            })
            .OnDrop(_ => { }));

        var overId = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-dragover");
        await view.TryInvokeHandlerAsync(overId!, Empty);
        view.RenderAsLiveRoot();

        Assert.False(captured!.IsDragging);
        Assert.Null(captured.TargetZone);
    }

    [Fact]
    public async Task Drag_end_clears_drag_state()
    {
        DragDropContext? captured = null;
        var view = new StubComponent(() => DragDrop
            .Body(ctx =>
            {
                captured = ctx;
                return Div[
                    Div.Draggable(true).OnDragStart(ctx.DragStart("z", 0))["s"],
                    Div.OnDragEnd(ctx.DragEnd)["e"]
                ];
            })
            .OnDrop(_ => { }));

        var html = view.RenderAsLiveRoot();
        await view.TryInvokeHandlerAsync(MarkupAssert.Attr(html, "data-rask-on-dragstart")!, Empty);
        await view.TryInvokeHandlerAsync(MarkupAssert.Attr(html, "data-rask-on-dragend")!, Empty);
        view.RenderAsLiveRoot();

        Assert.False(captured!.IsDragging);
        Assert.Null(captured.SourceZone);
    }

    [Fact]
    public async Task Drop_with_async_handler_awaits()
    {
        DragDropMove? captured = null;
        var view = new StubComponent(() => DragDrop
            .Body(ctx => Div[
                Div.Draggable(true).OnDragStart(ctx.DragStart("a", 1))["src"],
                Div.OnDrop(ctx.Drop("b", 0))["dst"]
            ])
            .OnDrop(async m =>
            {
                await Task.Yield();
                captured = m;
            }));

        var html = view.RenderAsLiveRoot();
        await view.TryInvokeHandlerAsync(MarkupAssert.Attr(html, "data-rask-on-dragstart")!, Empty);
        await view.TryInvokeHandlerAsync(MarkupAssert.Attr(html, "data-rask-on-drop")!, Empty);

        Assert.NotNull(captured);
        Assert.Equal("a", captured!.FromZone);
        Assert.Equal(1, captured.FromIndex);
        Assert.Equal("b", captured.ToZone);
        Assert.Equal(0, captured.ToIndex);
    }
}
