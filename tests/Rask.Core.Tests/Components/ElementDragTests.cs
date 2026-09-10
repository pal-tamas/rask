#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

public partial class ElementDragTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Draggable_True_EmitsDraggableAttribute() =>
        Assert.Equal("<div draggable=\"true\"></div>", Div.Draggable(true).ToHtml());

    [Fact]
    public void Draggable_NullOrFalse_EmitsNothing()
    {
        Assert.Equal("<div></div>", Div.ToHtml());
        Assert.Equal("<div></div>", Div.Draggable(false).ToHtml());
    }

    [Fact]
    public void Draggable_Getter_RoundTripsTriState()
    {
        // Draggable is backed by two flag bits (present + value) rather than a Nullable<bool> field;
        // the getter must still distinguish unset / false / true faithfully.
        Assert.Null(Div.Draggable);
        Assert.False(Div.Draggable(false).Draggable);
        Assert.True(Div.Draggable(true).Draggable);

        // Re-setting flips the value without leaking the previous state.
        //
        // RASK045 is exactly right about this shape and exactly wrong about this case: writing to a
        // property after a chain built the component is invisible from a call site, which is why the
        // rule exists — but the SETTER is what is under test here, and there is no way to exercise it
        // through the chain. Suppressed narrowly, which is the escape the rule documents.
#pragma warning disable RASK045 // the property setter is the subject, not a call site completing a component
        var d = Div.Draggable(true);
        d.Draggable = false;
        Assert.False(d.Draggable);
        d.Draggable = null;
        Assert.Null(d.Draggable);
#pragma warning restore RASK045
    }

    [Fact]
    public void DragHandlers_OutsideLiveContext_NotEmitted() =>
        // No LiveRenderContext (plain ToHtml): handlers can't register, so only the
        // static draggable attribute survives.
        Assert.Equal(
            "<div draggable=\"true\"></div>",
            Div.Draggable(true).OnDragStart(() => { }).OnDrop(() => { }).ToHtml());

    [Fact]
    public void DragHandlers_InsideLiveContext_EmitDataAttributesInRegistrationOrder()
    {
        var view = new StubComponent(() => Div
            .Id("d")
            .Class("x")
            .Draggable(true)
            .OnDragStart(() => { })
            .OnDragOver(() => { })
            .OnDrop(() => { })
            .OnDragEnd(() => { }));

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
        var view = new StubComponent(() => Div.OnDrop(() => { }));
        Assert.Equal("<div data-rask-on-drop=\"h0\"></div>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void DragHandlers_AsyncSiblingsEmit()
    {
        // Each drag event ships a Func<Task> async sibling; setting only the async variant still
        // registers the handler and emits the attribute, in dragstart → dragover → drop → dragend
        // order.
        var view = new StubComponent(() => Div
            .OnDragStart(() => Task.CompletedTask)
            .OnDragOver(() => Task.CompletedTask)
            .OnDrop(() => Task.CompletedTask)
            .OnDragEnd(() => Task.CompletedTask));
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
        var div = Div;
        Assert.Null(div.OnDragStart);
        Assert.Null(div.OnDragOver);
        Assert.Null(div.OnDrop);
        Assert.Null(div.OnDragEnd);
    }
}
