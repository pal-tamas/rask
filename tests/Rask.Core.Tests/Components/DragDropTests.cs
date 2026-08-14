using System.Text.Json;
using Rask.Core.DragAndDrop;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

public partial class DragDropTests : global::Rask.Core.RaskMarkup
{
    private static JsonElement Empty => JsonDocument.Parse("{}").RootElement;

    [Fact]
    public void Render_HeadlessNoOwnDom_OnlyEmitsBodyMarkup()
    {
        var view = new StubComponent(() => DragDrop.Body(ctx => Div["x"]));
        Assert.Equal("<div>x</div>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_NullBody_Throws()
    {
        var view = new StubComponent(() => DragDrop.Body(null!));
        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Drop_AfterDragStart_FiresOnDropWithMove()
    {
        DragDropMove? captured = null;
        var view = new StubComponent(() => DragDrop
            .Body(ctx => Div[
                Div.Draggable(true).OnDragStart(ctx.DragStart("zoneA", 2))["src"],
                Div.OnDropAsync(ctx.Drop("zoneB", 5))["dst"]
            ])
            .OnDrop(m => captured = m));

        var html = view.RenderAsLiveRoot();
        var startId = Markup.Attr(html, "data-rask-on-dragstart");
        var dropId = Markup.Attr(html, "data-rask-on-drop");

        await view.TryInvokeHandlerAsync(startId!, Empty);
        await view.TryInvokeHandlerAsync(dropId!, Empty);

        Assert.NotNull(captured);
        Assert.Equal("zoneA", captured!.FromZone);
        Assert.Equal(2, captured.FromIndex);
        Assert.Equal("zoneB", captured.ToZone);
        Assert.Equal(5, captured.ToIndex);
    }

    [Fact]
    public async Task Drop_WithoutDragStart_DoesNotFire()
    {
        var fired = false;
        var view = new StubComponent(() => DragDrop
            .Body(ctx => Div.OnDropAsync(ctx.Drop("z", 0))["dst"])
            .OnDrop(_ => fired = true));

        var dropId = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-drop");
        await view.TryInvokeHandlerAsync(dropId!, Empty);

        Assert.False(fired);
    }

    [Fact]
    public async Task DragOver_AfterDragStart_MarksDropTarget()
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
        var startId = Markup.Attr(html, "data-rask-on-dragstart");
        var overId = Markup.Attr(html, "data-rask-on-dragover");

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
    public async Task DragOver_WithoutDragStart_Ignored()
    {
        DragDropContext? captured = null;
        var view = new StubComponent(() => DragDrop
            .Body(ctx =>
            {
                captured = ctx;
                return Div.OnDragOver(ctx.DragOver("z", 1))["o"];
            })
            .OnDrop(_ => { }));

        var overId = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-dragover");
        await view.TryInvokeHandlerAsync(overId!, Empty);
        view.RenderAsLiveRoot();

        Assert.False(captured!.IsDragging);
        Assert.Null(captured.TargetZone);
    }

    [Fact]
    public async Task DragEnd_ClearsDragState()
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
        await view.TryInvokeHandlerAsync(Markup.Attr(html, "data-rask-on-dragstart")!, Empty);
        await view.TryInvokeHandlerAsync(Markup.Attr(html, "data-rask-on-dragend")!, Empty);
        view.RenderAsLiveRoot();

        Assert.False(captured!.IsDragging);
        Assert.Null(captured.SourceZone);
    }

    [Fact]
    public async Task Drop_WithAsyncHandler_Awaits()
    {
        DragDropMove? captured = null;
        var view = new StubComponent(() => DragDrop
            .Body(ctx => Div[
                Div.Draggable(true).OnDragStart(ctx.DragStart("a", 1))["src"],
                Div.OnDropAsync(ctx.Drop("b", 0))["dst"]
            ])
            .OnDropAsync(async m =>
            {
                await Task.Yield();
                captured = m;
            }));

        var html = view.RenderAsLiveRoot();
        await view.TryInvokeHandlerAsync(Markup.Attr(html, "data-rask-on-dragstart")!, Empty);
        await view.TryInvokeHandlerAsync(Markup.Attr(html, "data-rask-on-drop")!, Empty);

        Assert.NotNull(captured);
        Assert.Equal("a", captured!.FromZone);
        Assert.Equal(1, captured.FromIndex);
        Assert.Equal("b", captured.ToZone);
        Assert.Equal(0, captured.ToIndex);
    }
}
