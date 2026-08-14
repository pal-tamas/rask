using System.Text;
using Rask.Core.Live;

namespace Rask.Core;

// The extended GlobalEventHandlers surface, mirroring the DOM mixin every HTMLElement implements: the
// `on*` handlers live on Element so EVERY tag gets them (Span(OnMouseEnter: …), Li(OnContextMenu: …)),
// not just a hand-picked few. Each event is a sync `OnXxx` (Action / Action<TArgs>) + async
// `OnXxxAsync` (Func<Task> / Func<TArgs, Task>) pair coalesced over ONE slot in the shared
// LiveState DomEvents dictionary (see Component.GetDomEvent/SetDomEvent) — so a plain element that wires
// nothing keeps `_live` null and pays no per-instance footprint, and the generated factory re-applying
// both views (one null) every render never clobbers the handler the caller set.
//
// Pre-existing handlers that already had bespoke storage (OnKeyDown/Up, OnDragStart/Over/Drop/End,
// OnClick/OnScroll were tag-local) are unified here: OnClick/OnScroll now flow through this store too,
// while keyboard/drag keep their original slots. Emitted in WriteAttributes (Element.cs) via
// EmitDomEvents, after the drag/keyboard hooks, in the fixed GlobalEventOrder below.
public abstract partial class Element
{
    // Fixed emit order so the serialized attribute sequence is deterministic (tests assert it). The
    // drag (dragstart/over/drop/end) and keyboard (keydown/up) hooks lead — preserving the order the
    // bespoke per-event storage used before they were merged into this unified store. Media events are
    // NOT here — they belong to HtmlMediaElement, which emits them itself.
    private static readonly string[] GlobalEventOrder =
    {
        "dragstart", "dragover", "drop", "dragend",
        "keydown", "keyup",
        "click", "dblclick", "mousedown", "mouseup", "mousemove", "mouseenter", "mouseleave", "mouseover",
        "mouseout", "contextmenu", "wheel",
        "pointerdown", "pointerup", "pointermove", "pointerenter", "pointerleave", "pointerover",
        "pointerout", "pointercancel",
        "touchstart", "touchend", "touchmove", "touchcancel",
        "focus", "blur", "focusin", "focusout",
        "drag", "dragenter", "dragleave",
        "copy", "cut", "paste",
        "beforeinput", "select", "invalid", "reset",
        "scroll"
    };

    // Unified backing store for the WHOLE event surface — drag, keyboard, click, scroll, mouse, pointer,
    // touch, focus, clipboard, wheel, plus the HTMLMediaElement events. A single keyed dictionary instead
    // of ~50 named fields, kept as a DIRECT Element field (not hoisted into LiveState): a click-bearing
    // leaf would otherwise force a whole LiveState allocation, whereas this allocates only the small dict
    // on first handler. A plain element that wires nothing keeps `_domEvents` null and pays one extra
    // reference field. Each event name maps to ONE slot holding the delegate plus an IsAsync flag, so a
    // null re-applied by the factory clears only its own kind WITHOUT a per-render reflection probe.
    private Dictionary<string, (Delegate Action, bool IsAsync)>? _domEvents;

    // Render-hotpath early-out: WriteAttributes asks this before iterating the ordered event list. A
    // plain element answers false in one null check, so the per-render cost stays at zero.
    private protected bool HasDomEvents => _domEvents is { Count: > 0 };

    private protected Delegate? GetDomEvent(string name) =>
        _domEvents is { } map && map.TryGetValue(name, out var slot) ? slot.Action : null;

    // ---- Typed views over the slot ----------------------------------------------------------------
    //
    // The dictionary holds every handler as a bare `Delegate`, because that is what dispatch needs; the
    // properties below read one back at the type they were declared with. A slot holding the other kind
    // (an async handler read through the sync view) reads back as null, which is what the `as` cast says.
    //
    // These used to hand back a CARRIER — a struct wrapping the delegate — so that `Div.OnClick(handler)`
    // could reach a setter of the same name instead of trying to invoke the property (CS1593). The chain
    // receives on `Build<TComponent>` now, so the property is not on the receiver and cannot swallow its
    // setter; the carrier, and the null-preservation dance its implicit conversion forced, are both gone.
    private protected Action? SyncHandler(string name) => GetDomEvent(name) as Action;

    private protected Func<Task>? AsyncHandler(string name) => GetDomEvent(name) as Func<Task>;

    private protected Action<TArgs>? SyncHandler<TArgs>(string name) => GetDomEvent(name) as Action<TArgs>;

    private protected Func<TArgs, Task>? AsyncHandler<TArgs>(string name) =>
        GetDomEvent(name) as Func<TArgs, Task>;

    // Sync handler always wins: setting it overwrites whatever's there, so `OnClick` beats `OnClickAsync`
    // when both are supplied the same render. A null clears the slot only when it currently holds a sync
    // handler. (The RASK027 analyzer flags wiring both siblings at once; this is the runtime tiebreaker.)
    private protected void SetDomEventSync(string name, Delegate? value)
    {
        if (value is not null)
        {
            (_domEvents ??= new Dictionary<string, (Delegate, bool)>(StringComparer.Ordinal))[name] = (value, false);
        }
        else if (_domEvents is { } map && map.TryGetValue(name, out var slot) && !slot.IsAsync)
        {
            map.Remove(name);
        }
    }

    // Async handler defers to a sync one: it sets the slot only when no sync handler holds it. A null
    // clears the slot only when it currently holds an async handler.
    private protected void SetDomEventAsync(string name, Delegate? value)
    {
        if (value is not null)
        {
            var map = _domEvents ??= new Dictionary<string, (Delegate, bool)>(StringComparer.Ordinal);
            if (!(map.TryGetValue(name, out var slot) && !slot.IsAsync))
            {
                map[name] = (value, true);
            }
        }
        else if (_domEvents is { } existing && existing.TryGetValue(name, out var current) && current.IsAsync)
        {
            existing.Remove(name);
        }
    }

    // ---- Drag & drop (parameterless; the dragged item's identity rides the handler's closure — see
    //      the headless DragDrop primitive). dragstart/over/drop/end here; drag/dragenter/dragleave below. ----

    public Action? OnDragStart { get => SyncHandler("dragstart"); set => SetDomEventSync("dragstart", value); }
    public Func<Task>? OnDragStartAsync { get => AsyncHandler("dragstart"); set => SetDomEventAsync("dragstart", value); }

    public Action? OnDragOver { get => SyncHandler("dragover"); set => SetDomEventSync("dragover", value); }
    public Func<Task>? OnDragOverAsync { get => AsyncHandler("dragover"); set => SetDomEventAsync("dragover", value); }

    public Action? OnDrop { get => SyncHandler("drop"); set => SetDomEventSync("drop", value); }
    public Func<Task>? OnDropAsync { get => AsyncHandler("drop"); set => SetDomEventAsync("drop", value); }

    public Action? OnDragEnd { get => SyncHandler("dragend"); set => SetDomEventSync("dragend", value); }
    public Func<Task>? OnDragEndAsync { get => AsyncHandler("dragend"); set => SetDomEventAsync("dragend", value); }

    // ---- Keyboard (KeyboardEventArgs: key/code/modifiers/repeat; the client never preventDefaults) ----

    public Action<KeyboardEventArgs>? OnKeyDown { get => SyncHandler<KeyboardEventArgs>("keydown"); set => SetDomEventSync("keydown", value); }
    public Func<KeyboardEventArgs, Task>? OnKeyDownAsync { get => AsyncHandler<KeyboardEventArgs>("keydown"); set => SetDomEventAsync("keydown", value); }

    public Action<KeyboardEventArgs>? OnKeyUp { get => SyncHandler<KeyboardEventArgs>("keyup"); set => SetDomEventSync("keyup", value); }
    public Func<KeyboardEventArgs, Task>? OnKeyUpAsync { get => AsyncHandler<KeyboardEventArgs>("keyup"); set => SetDomEventAsync("keyup", value); }

    // ---- Mouse events (MouseEventArgs: button/buttons, client/screen/page/offset/movement coords, modifiers) ----

    /// <summary>Click. Parameterless (modifier/coordinate-free) for source compatibility — use the mouse
    /// events below for geometry. The client still <c>preventDefault</c>s anchor navigation on click.</summary>
    public Action? OnClick { get => SyncHandler("click"); set => SetDomEventSync("click", value); }
    public Func<Task>? OnClickAsync { get => AsyncHandler("click"); set => SetDomEventAsync("click", value); }

    public Action<MouseEventArgs>? OnDoubleClick { get => SyncHandler<MouseEventArgs>("dblclick"); set => SetDomEventSync("dblclick", value); }
    public Func<MouseEventArgs, Task>? OnDoubleClickAsync { get => AsyncHandler<MouseEventArgs>("dblclick"); set => SetDomEventAsync("dblclick", value); }

    public Action<MouseEventArgs>? OnMouseDown { get => SyncHandler<MouseEventArgs>("mousedown"); set => SetDomEventSync("mousedown", value); }
    public Func<MouseEventArgs, Task>? OnMouseDownAsync { get => AsyncHandler<MouseEventArgs>("mousedown"); set => SetDomEventAsync("mousedown", value); }

    public Action<MouseEventArgs>? OnMouseUp { get => SyncHandler<MouseEventArgs>("mouseup"); set => SetDomEventSync("mouseup", value); }
    public Func<MouseEventArgs, Task>? OnMouseUpAsync { get => AsyncHandler<MouseEventArgs>("mouseup"); set => SetDomEventAsync("mouseup", value); }

    public Action<MouseEventArgs>? OnMouseMove { get => SyncHandler<MouseEventArgs>("mousemove"); set => SetDomEventSync("mousemove", value); }
    public Func<MouseEventArgs, Task>? OnMouseMoveAsync { get => AsyncHandler<MouseEventArgs>("mousemove"); set => SetDomEventAsync("mousemove", value); }

    /// <summary>Pointer entered this element (does not fire for descendants). Simulated client-side via
    /// <c>mouseover</c> + relatedTarget boundary, since <c>mouseenter</c> itself does not delegate.</summary>
    public Action<MouseEventArgs>? OnMouseEnter { get => SyncHandler<MouseEventArgs>("mouseenter"); set => SetDomEventSync("mouseenter", value); }
    public Func<MouseEventArgs, Task>? OnMouseEnterAsync { get => AsyncHandler<MouseEventArgs>("mouseenter"); set => SetDomEventAsync("mouseenter", value); }

    public Action<MouseEventArgs>? OnMouseLeave { get => SyncHandler<MouseEventArgs>("mouseleave"); set => SetDomEventSync("mouseleave", value); }
    public Func<MouseEventArgs, Task>? OnMouseLeaveAsync { get => AsyncHandler<MouseEventArgs>("mouseleave"); set => SetDomEventAsync("mouseleave", value); }

    public Action<MouseEventArgs>? OnMouseOver { get => SyncHandler<MouseEventArgs>("mouseover"); set => SetDomEventSync("mouseover", value); }
    public Func<MouseEventArgs, Task>? OnMouseOverAsync { get => AsyncHandler<MouseEventArgs>("mouseover"); set => SetDomEventAsync("mouseover", value); }

    public Action<MouseEventArgs>? OnMouseOut { get => SyncHandler<MouseEventArgs>("mouseout"); set => SetDomEventSync("mouseout", value); }
    public Func<MouseEventArgs, Task>? OnMouseOutAsync { get => AsyncHandler<MouseEventArgs>("mouseout"); set => SetDomEventAsync("mouseout", value); }

    /// <summary>Right-click / context menu. The client <c>preventDefault</c>s so the browser menu is
    /// suppressed when you handle it.</summary>
    public Action<MouseEventArgs>? OnContextMenu { get => SyncHandler<MouseEventArgs>("contextmenu"); set => SetDomEventSync("contextmenu", value); }
    public Func<MouseEventArgs, Task>? OnContextMenuAsync { get => AsyncHandler<MouseEventArgs>("contextmenu"); set => SetDomEventAsync("contextmenu", value); }

    // ---- Wheel ----

    public Action<WheelEventArgs>? OnWheel { get => SyncHandler<WheelEventArgs>("wheel"); set => SetDomEventSync("wheel", value); }
    public Func<WheelEventArgs, Task>? OnWheelAsync { get => AsyncHandler<WheelEventArgs>("wheel"); set => SetDomEventAsync("wheel", value); }

    // ---- Pointer events (PointerEventArgs: mouse geometry + pointerId/pressure/tilt/pointerType/isPrimary) ----

    public Action<PointerEventArgs>? OnPointerDown { get => SyncHandler<PointerEventArgs>("pointerdown"); set => SetDomEventSync("pointerdown", value); }
    public Func<PointerEventArgs, Task>? OnPointerDownAsync { get => AsyncHandler<PointerEventArgs>("pointerdown"); set => SetDomEventAsync("pointerdown", value); }

    public Action<PointerEventArgs>? OnPointerUp { get => SyncHandler<PointerEventArgs>("pointerup"); set => SetDomEventSync("pointerup", value); }
    public Func<PointerEventArgs, Task>? OnPointerUpAsync { get => AsyncHandler<PointerEventArgs>("pointerup"); set => SetDomEventAsync("pointerup", value); }

    public Action<PointerEventArgs>? OnPointerMove { get => SyncHandler<PointerEventArgs>("pointermove"); set => SetDomEventSync("pointermove", value); }
    public Func<PointerEventArgs, Task>? OnPointerMoveAsync { get => AsyncHandler<PointerEventArgs>("pointermove"); set => SetDomEventAsync("pointermove", value); }

    public Action<PointerEventArgs>? OnPointerEnter { get => SyncHandler<PointerEventArgs>("pointerenter"); set => SetDomEventSync("pointerenter", value); }
    public Func<PointerEventArgs, Task>? OnPointerEnterAsync { get => AsyncHandler<PointerEventArgs>("pointerenter"); set => SetDomEventAsync("pointerenter", value); }

    public Action<PointerEventArgs>? OnPointerLeave { get => SyncHandler<PointerEventArgs>("pointerleave"); set => SetDomEventSync("pointerleave", value); }
    public Func<PointerEventArgs, Task>? OnPointerLeaveAsync { get => AsyncHandler<PointerEventArgs>("pointerleave"); set => SetDomEventAsync("pointerleave", value); }

    public Action<PointerEventArgs>? OnPointerOver { get => SyncHandler<PointerEventArgs>("pointerover"); set => SetDomEventSync("pointerover", value); }
    public Func<PointerEventArgs, Task>? OnPointerOverAsync { get => AsyncHandler<PointerEventArgs>("pointerover"); set => SetDomEventAsync("pointerover", value); }

    public Action<PointerEventArgs>? OnPointerOut { get => SyncHandler<PointerEventArgs>("pointerout"); set => SetDomEventSync("pointerout", value); }
    public Func<PointerEventArgs, Task>? OnPointerOutAsync { get => AsyncHandler<PointerEventArgs>("pointerout"); set => SetDomEventAsync("pointerout", value); }

    public Action<PointerEventArgs>? OnPointerCancel { get => SyncHandler<PointerEventArgs>("pointercancel"); set => SetDomEventSync("pointercancel", value); }
    public Func<PointerEventArgs, Task>? OnPointerCancelAsync { get => AsyncHandler<PointerEventArgs>("pointercancel"); set => SetDomEventAsync("pointercancel", value); }

    // ---- Touch events (TouchEventArgs: active touch count + first-touch coords + modifiers) ----

    public Action<TouchEventArgs>? OnTouchStart { get => SyncHandler<TouchEventArgs>("touchstart"); set => SetDomEventSync("touchstart", value); }
    public Func<TouchEventArgs, Task>? OnTouchStartAsync { get => AsyncHandler<TouchEventArgs>("touchstart"); set => SetDomEventAsync("touchstart", value); }

    public Action<TouchEventArgs>? OnTouchEnd { get => SyncHandler<TouchEventArgs>("touchend"); set => SetDomEventSync("touchend", value); }
    public Func<TouchEventArgs, Task>? OnTouchEndAsync { get => AsyncHandler<TouchEventArgs>("touchend"); set => SetDomEventAsync("touchend", value); }

    public Action<TouchEventArgs>? OnTouchMove { get => SyncHandler<TouchEventArgs>("touchmove"); set => SetDomEventSync("touchmove", value); }
    public Func<TouchEventArgs, Task>? OnTouchMoveAsync { get => AsyncHandler<TouchEventArgs>("touchmove"); set => SetDomEventAsync("touchmove", value); }

    public Action<TouchEventArgs>? OnTouchCancel { get => SyncHandler<TouchEventArgs>("touchcancel"); set => SetDomEventSync("touchcancel", value); }
    public Func<TouchEventArgs, Task>? OnTouchCancelAsync { get => AsyncHandler<TouchEventArgs>("touchcancel"); set => SetDomEventAsync("touchcancel", value); }

    // ---- Focus events (parameterless; focus/blur reach Element via capture-phase delegation) ----

    public Action? OnFocus { get => SyncHandler("focus"); set => SetDomEventSync("focus", value); }
    public Func<Task>? OnFocusAsync { get => AsyncHandler("focus"); set => SetDomEventAsync("focus", value); }

    public Action? OnBlur { get => SyncHandler("blur"); set => SetDomEventSync("blur", value); }
    public Func<Task>? OnBlurAsync { get => AsyncHandler("blur"); set => SetDomEventAsync("blur", value); }

    public Action? OnFocusIn { get => SyncHandler("focusin"); set => SetDomEventSync("focusin", value); }
    public Func<Task>? OnFocusInAsync { get => AsyncHandler("focusin"); set => SetDomEventAsync("focusin", value); }

    public Action? OnFocusOut { get => SyncHandler("focusout"); set => SetDomEventSync("focusout", value); }
    public Func<Task>? OnFocusOutAsync { get => AsyncHandler("focusout"); set => SetDomEventAsync("focusout", value); }

    // ---- Drag events that complete the set (dragstart/over/drop/end already exist on Element) ----

    public Action? OnDrag { get => SyncHandler("drag"); set => SetDomEventSync("drag", value); }
    public Func<Task>? OnDragAsync { get => AsyncHandler("drag"); set => SetDomEventAsync("drag", value); }

    public Action? OnDragEnter { get => SyncHandler("dragenter"); set => SetDomEventSync("dragenter", value); }
    public Func<Task>? OnDragEnterAsync { get => AsyncHandler("dragenter"); set => SetDomEventAsync("dragenter", value); }

    public Action? OnDragLeave { get => SyncHandler("dragleave"); set => SetDomEventSync("dragleave", value); }
    public Func<Task>? OnDragLeaveAsync { get => AsyncHandler("dragleave"); set => SetDomEventAsync("dragleave", value); }

    // ---- Clipboard events (ClipboardEventArgs: the plain-text payload read during the event) ----

    public Action<ClipboardEventArgs>? OnCopy { get => SyncHandler<ClipboardEventArgs>("copy"); set => SetDomEventSync("copy", value); }
    public Func<ClipboardEventArgs, Task>? OnCopyAsync { get => AsyncHandler<ClipboardEventArgs>("copy"); set => SetDomEventAsync("copy", value); }

    public Action<ClipboardEventArgs>? OnCut { get => SyncHandler<ClipboardEventArgs>("cut"); set => SetDomEventSync("cut", value); }
    public Func<ClipboardEventArgs, Task>? OnCutAsync { get => AsyncHandler<ClipboardEventArgs>("cut"); set => SetDomEventAsync("cut", value); }

    public Action<ClipboardEventArgs>? OnPaste { get => SyncHandler<ClipboardEventArgs>("paste"); set => SetDomEventSync("paste", value); }
    public Func<ClipboardEventArgs, Task>? OnPasteAsync { get => AsyncHandler<ClipboardEventArgs>("paste"); set => SetDomEventAsync("paste", value); }

    // ---- Remaining form-ish events (beforeinput carries the inserted text; select/invalid/reset are bare) ----

    public Action<string>? OnBeforeInput { get => SyncHandler<string>("beforeinput"); set => SetDomEventSync("beforeinput", value); }
    public Func<string, Task>? OnBeforeInputAsync { get => AsyncHandler<string>("beforeinput"); set => SetDomEventAsync("beforeinput", value); }

    public Action? OnSelect { get => SyncHandler("select"); set => SetDomEventSync("select", value); }
    public Func<Task>? OnSelectAsync { get => AsyncHandler("select"); set => SetDomEventAsync("select", value); }

    public Action? OnInvalid { get => SyncHandler("invalid"); set => SetDomEventSync("invalid", value); }
    public Func<Task>? OnInvalidAsync { get => AsyncHandler("invalid"); set => SetDomEventAsync("invalid", value); }

    public Action? OnReset { get => SyncHandler("reset"); set => SetDomEventSync("reset", value); }
    public Func<Task>? OnResetAsync { get => AsyncHandler("reset"); set => SetDomEventAsync("reset", value); }

    // ---- Scroll (ScrollEvent: scrollTop/clientHeight/scrollHeight; rAF-coalesced client-side) ----

    public Action<ScrollEvent>? OnScroll { get => SyncHandler<ScrollEvent>("scroll"); set => SetDomEventSync("scroll", value); }
    public Func<ScrollEvent, Task>? OnScrollAsync { get => AsyncHandler<ScrollEvent>("scroll"); set => SetDomEventAsync("scroll", value); }

    // Emits every wired GlobalEventHandlers hook as data-rask-on-{event}, in GlobalEventOrder, so the
    // serialized attribute sequence is deterministic. Early-outs in one null check for a plain element.
    internal void EmitDomEvents(StringBuilder sb, LiveRenderContext ctx)
    {
        if (!HasDomEvents)
        {
            return;
        }

        foreach (var name in GlobalEventOrder)
        {
            EmitDomEvent(sb, ctx, name);
        }
    }

    // Emits one event hook if a handler is wired for it. Shared with HtmlMediaElement, which calls it for
    // the media events (play/pause/…) that don't belong on the universal Element surface.
    private protected void EmitDomEvent(StringBuilder sb, LiveRenderContext ctx, string name)
    {
        if (GetDomEvent(name) is { } handler)
        {
            AppendAttr(sb, "data-rask-on-", name, ctx.RegisterHandler(handler));
        }
    }
}
