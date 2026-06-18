#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

public class ElementDragTests
{
    [Fact]
    public void Draggable_True_EmitsDraggableAttribute() =>
        Assert.Equal("<div draggable=\"true\"></div>", Div(Draggable: true).ToHtml());

    [Fact]
    public void Draggable_NullOrFalse_EmitsNothing()
    {
        Assert.Equal("<div></div>", Div().ToHtml());
        Assert.Equal("<div></div>", Div(Draggable: false).ToHtml());
    }

    [Fact]
    public void DragHandlers_OutsideLiveContext_NotEmitted() =>
        // No LiveRenderContext (plain ToHtml): handlers can't register, so only the
        // static draggable attribute survives.
        Assert.Equal(
            "<div draggable=\"true\"></div>",
            Div(Draggable: true, OnDragStart: new Action(() => { }), OnDrop: new Action(() => { })).ToHtml());

    [Fact]
    public void DragHandlers_InsideLiveContext_EmitDataAttributesInRegistrationOrder()
    {
        var view = new StubComponent(() => Div(
            Id: "d",
            Class: "x",
            Draggable: true,
            OnDragStart: new Action(() => { }),
            OnDragOver: new Action(() => { }),
            OnDrop: new Action(() => { }),
            OnDragEnd: new Action(() => { })));

        // Universal attrs first (id, class), then draggable, then the drag handler hooks in
        // dragstart → dragover → drop → dragend order (matching RegisterHandler id assignment).
        Assert.Equal(
            "<div id=\"d\" class=\"x\" draggable=\"true\" " +
            "data-rask-on-dragstart=\"h0\" data-rask-on-dragover=\"h1\" " +
            "data-rask-on-drop=\"h2\" data-rask-on-dragend=\"h3\"></div>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void DragHandlers_OnlyNonNullEmitted()
    {
        var view = new StubComponent(() => Div(OnDrop: new Action(() => { })));
        Assert.Equal("<div data-rask-on-drop=\"h0\"></div>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void DragHandlers_AsyncSiblingsEmit()
    {
        // Each drag event ships a Func<Task> async sibling; setting only the async variant still
        // registers the handler and emits the attribute, in dragstart → dragover → drop → dragend
        // order.
        var view = new StubComponent(() => Div(
            OnDragStartAsync: () => Task.CompletedTask,
            OnDragOverAsync: () => Task.CompletedTask,
            OnDropAsync: () => Task.CompletedTask,
            OnDragEndAsync: () => Task.CompletedTask));
        Assert.Equal(
            "<div data-rask-on-dragstart=\"h0\" data-rask-on-dragover=\"h1\" " +
            "data-rask-on-drop=\"h2\" data-rask-on-dragend=\"h3\"></div>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void UnsetDragHandlers_AddNoFootprint()
    {
        // Drag handlers are hoisted into the lazy LiveState (like the keyboard handlers and
        // Ref/Role/Aria), so an element that wires none of them keeps every slot null and pays no
        // per-instance footprint.
        var div = Div();
        Assert.Null(div.OnDragStart);
        Assert.Null(div.OnDragStartAsync);
        Assert.Null(div.OnDragOver);
        Assert.Null(div.OnDragOverAsync);
        Assert.Null(div.OnDrop);
        Assert.Null(div.OnDropAsync);
        Assert.Null(div.OnDragEnd);
        Assert.Null(div.OnDragEndAsync);
    }
}
