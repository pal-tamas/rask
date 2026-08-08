using System.Text;
using Rask.Core.Live;

namespace Rask.Core;

// The extended GlobalEventHandlers surface, mirroring the DOM mixin every HTMLElement implements: the
// `on*` handlers live on Element so EVERY tag gets them (Span(OnMouseEnter: …), Li(OnContextMenu: …)),
// not just a hand-picked few. Each event is a sync `OnXxx` (Handler / Handler<TArgs>) + async
// `OnXxxAsync` (HandlerAsync / HandlerAsync<TArgs>) pair coalesced over ONE slot in the shared
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
    private Dictionary<string, (Delegate Handler, bool IsAsync)>? _domEvents;

    // Render-hotpath early-out: WriteAttributes asks this before iterating the ordered event list. A
    // plain element answers false in one null check, so the per-render cost stays at zero.
    private protected bool HasDomEvents => _domEvents is { Count: > 0 };

    private protected Delegate? GetDomEvent(string name) =>
        _domEvents is { } map && map.TryGetValue(name, out var slot) ? slot.Handler : null;

    // ---- Carrier views over the slot ------------------------------------------------------------
    //
    // Every event property below is typed as a CARRIER (Handler / HandlerAsync / their argument-taking
    // siblings) rather than as the delegate itself, so the builder setter can keep the property's own
    // name: a delegate-typed member IS invocable, so `Div.OnClick(handler)` would bind to the property
    // and fail with CS1593 (which is why the setter used to be `.Click(…)`). A struct is not invocable,
    // so the same-named extension binds — see Rask.Core.Handler.
    //
    // The carrier is a view, never storage: the dictionary keeps holding the raw delegate, so handler
    // registration, dispatch and the `as Callback` kind test are untouched, and wrapping/unwrapping a
    // readonly struct around a reference allocates nothing. A slot holding the other kind (an async
    // handler read through the sync view) reads back as null, exactly as the `as` cast did before.
    //
    // The unset branch is CAST, never a bare `null`: the carrier's implicit conversion accepts a null
    // delegate, so `cond ? new Handler(fn) : null` has a natural type of `Handler` — the null literal
    // would go through the operator and hand back a non-null carrier wrapping a null delegate, and an
    // unset handler would stop reading back as unset. The cast gives the conditional the nullable type
    // and lets the standard null-literal conversion win.
    private protected Handler? SyncHandler(string name) =>
        GetDomEvent(name) is Callback fn ? new Handler(fn) : (Handler?)null;

    private protected HandlerAsync? AsyncHandler(string name) =>
        GetDomEvent(name) is CallbackAsync fn ? new HandlerAsync(fn) : (HandlerAsync?)null;

    private protected Handler<TArgs>? SyncHandler<TArgs>(string name) =>
        GetDomEvent(name) is Callback<TArgs> fn ? new Handler<TArgs>(fn) : (Handler<TArgs>?)null;

    private protected HandlerAsync<TArgs>? AsyncHandler<TArgs>(string name) =>
        GetDomEvent(name) is CallbackAsync<TArgs> fn ? new HandlerAsync<TArgs>(fn) : (HandlerAsync<TArgs>?)null;

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

    public Handler? OnDragStart { get => SyncHandler("dragstart"); set => SetDomEventSync("dragstart", value?.Fn); }
    public HandlerAsync? OnDragStartAsync { get => AsyncHandler("dragstart"); set => SetDomEventAsync("dragstart", value?.Fn); }

    public Handler? OnDragOver { get => SyncHandler("dragover"); set => SetDomEventSync("dragover", value?.Fn); }
    public HandlerAsync? OnDragOverAsync { get => AsyncHandler("dragover"); set => SetDomEventAsync("dragover", value?.Fn); }

    public Handler? OnDrop { get => SyncHandler("drop"); set => SetDomEventSync("drop", value?.Fn); }
    public HandlerAsync? OnDropAsync { get => AsyncHandler("drop"); set => SetDomEventAsync("drop", value?.Fn); }

    public Handler? OnDragEnd { get => SyncHandler("dragend"); set => SetDomEventSync("dragend", value?.Fn); }
    public HandlerAsync? OnDragEndAsync { get => AsyncHandler("dragend"); set => SetDomEventAsync("dragend", value?.Fn); }

    // ---- Keyboard (KeyboardEventArgs: key/code/modifiers/repeat; the client never preventDefaults) ----

    public Handler<KeyboardEventArgs>? OnKeyDown { get => SyncHandler<KeyboardEventArgs>("keydown"); set => SetDomEventSync("keydown", value?.Fn); }
    public HandlerAsync<KeyboardEventArgs>? OnKeyDownAsync { get => AsyncHandler<KeyboardEventArgs>("keydown"); set => SetDomEventAsync("keydown", value?.Fn); }

    public Handler<KeyboardEventArgs>? OnKeyUp { get => SyncHandler<KeyboardEventArgs>("keyup"); set => SetDomEventSync("keyup", value?.Fn); }
    public HandlerAsync<KeyboardEventArgs>? OnKeyUpAsync { get => AsyncHandler<KeyboardEventArgs>("keyup"); set => SetDomEventAsync("keyup", value?.Fn); }

    // ---- Mouse events (MouseEventArgs: button/buttons, client/screen/page/offset/movement coords, modifiers) ----

    /// <summary>Click. Parameterless (modifier/coordinate-free) for source compatibility — use the mouse
    /// events below for geometry. The client still <c>preventDefault</c>s anchor navigation on click.</summary>
    public Handler? OnClick { get => SyncHandler("click"); set => SetDomEventSync("click", value?.Fn); }
    public HandlerAsync? OnClickAsync { get => AsyncHandler("click"); set => SetDomEventAsync("click", value?.Fn); }

    public Handler<MouseEventArgs>? OnDoubleClick { get => SyncHandler<MouseEventArgs>("dblclick"); set => SetDomEventSync("dblclick", value?.Fn); }
    public HandlerAsync<MouseEventArgs>? OnDoubleClickAsync { get => AsyncHandler<MouseEventArgs>("dblclick"); set => SetDomEventAsync("dblclick", value?.Fn); }

    public Handler<MouseEventArgs>? OnMouseDown { get => SyncHandler<MouseEventArgs>("mousedown"); set => SetDomEventSync("mousedown", value?.Fn); }
    public HandlerAsync<MouseEventArgs>? OnMouseDownAsync { get => AsyncHandler<MouseEventArgs>("mousedown"); set => SetDomEventAsync("mousedown", value?.Fn); }

    public Handler<MouseEventArgs>? OnMouseUp { get => SyncHandler<MouseEventArgs>("mouseup"); set => SetDomEventSync("mouseup", value?.Fn); }
    public HandlerAsync<MouseEventArgs>? OnMouseUpAsync { get => AsyncHandler<MouseEventArgs>("mouseup"); set => SetDomEventAsync("mouseup", value?.Fn); }

    public Handler<MouseEventArgs>? OnMouseMove { get => SyncHandler<MouseEventArgs>("mousemove"); set => SetDomEventSync("mousemove", value?.Fn); }
    public HandlerAsync<MouseEventArgs>? OnMouseMoveAsync { get => AsyncHandler<MouseEventArgs>("mousemove"); set => SetDomEventAsync("mousemove", value?.Fn); }

    /// <summary>Pointer entered this element (does not fire for descendants). Simulated client-side via
    /// <c>mouseover</c> + relatedTarget boundary, since <c>mouseenter</c> itself does not delegate.</summary>
    public Handler<MouseEventArgs>? OnMouseEnter { get => SyncHandler<MouseEventArgs>("mouseenter"); set => SetDomEventSync("mouseenter", value?.Fn); }
    public HandlerAsync<MouseEventArgs>? OnMouseEnterAsync { get => AsyncHandler<MouseEventArgs>("mouseenter"); set => SetDomEventAsync("mouseenter", value?.Fn); }

    public Handler<MouseEventArgs>? OnMouseLeave { get => SyncHandler<MouseEventArgs>("mouseleave"); set => SetDomEventSync("mouseleave", value?.Fn); }
    public HandlerAsync<MouseEventArgs>? OnMouseLeaveAsync { get => AsyncHandler<MouseEventArgs>("mouseleave"); set => SetDomEventAsync("mouseleave", value?.Fn); }

    public Handler<MouseEventArgs>? OnMouseOver { get => SyncHandler<MouseEventArgs>("mouseover"); set => SetDomEventSync("mouseover", value?.Fn); }
    public HandlerAsync<MouseEventArgs>? OnMouseOverAsync { get => AsyncHandler<MouseEventArgs>("mouseover"); set => SetDomEventAsync("mouseover", value?.Fn); }

    public Handler<MouseEventArgs>? OnMouseOut { get => SyncHandler<MouseEventArgs>("mouseout"); set => SetDomEventSync("mouseout", value?.Fn); }
    public HandlerAsync<MouseEventArgs>? OnMouseOutAsync { get => AsyncHandler<MouseEventArgs>("mouseout"); set => SetDomEventAsync("mouseout", value?.Fn); }

    /// <summary>Right-click / context menu. The client <c>preventDefault</c>s so the browser menu is
    /// suppressed when you handle it.</summary>
    public Handler<MouseEventArgs>? OnContextMenu { get => SyncHandler<MouseEventArgs>("contextmenu"); set => SetDomEventSync("contextmenu", value?.Fn); }
    public HandlerAsync<MouseEventArgs>? OnContextMenuAsync { get => AsyncHandler<MouseEventArgs>("contextmenu"); set => SetDomEventAsync("contextmenu", value?.Fn); }

    // ---- Wheel ----

    public Handler<WheelEventArgs>? OnWheel { get => SyncHandler<WheelEventArgs>("wheel"); set => SetDomEventSync("wheel", value?.Fn); }
    public HandlerAsync<WheelEventArgs>? OnWheelAsync { get => AsyncHandler<WheelEventArgs>("wheel"); set => SetDomEventAsync("wheel", value?.Fn); }

    // ---- Pointer events (PointerEventArgs: mouse geometry + pointerId/pressure/tilt/pointerType/isPrimary) ----

    public Handler<PointerEventArgs>? OnPointerDown { get => SyncHandler<PointerEventArgs>("pointerdown"); set => SetDomEventSync("pointerdown", value?.Fn); }
    public HandlerAsync<PointerEventArgs>? OnPointerDownAsync { get => AsyncHandler<PointerEventArgs>("pointerdown"); set => SetDomEventAsync("pointerdown", value?.Fn); }

    public Handler<PointerEventArgs>? OnPointerUp { get => SyncHandler<PointerEventArgs>("pointerup"); set => SetDomEventSync("pointerup", value?.Fn); }
    public HandlerAsync<PointerEventArgs>? OnPointerUpAsync { get => AsyncHandler<PointerEventArgs>("pointerup"); set => SetDomEventAsync("pointerup", value?.Fn); }

    public Handler<PointerEventArgs>? OnPointerMove { get => SyncHandler<PointerEventArgs>("pointermove"); set => SetDomEventSync("pointermove", value?.Fn); }
    public HandlerAsync<PointerEventArgs>? OnPointerMoveAsync { get => AsyncHandler<PointerEventArgs>("pointermove"); set => SetDomEventAsync("pointermove", value?.Fn); }

    public Handler<PointerEventArgs>? OnPointerEnter { get => SyncHandler<PointerEventArgs>("pointerenter"); set => SetDomEventSync("pointerenter", value?.Fn); }
    public HandlerAsync<PointerEventArgs>? OnPointerEnterAsync { get => AsyncHandler<PointerEventArgs>("pointerenter"); set => SetDomEventAsync("pointerenter", value?.Fn); }

    public Handler<PointerEventArgs>? OnPointerLeave { get => SyncHandler<PointerEventArgs>("pointerleave"); set => SetDomEventSync("pointerleave", value?.Fn); }
    public HandlerAsync<PointerEventArgs>? OnPointerLeaveAsync { get => AsyncHandler<PointerEventArgs>("pointerleave"); set => SetDomEventAsync("pointerleave", value?.Fn); }

    public Handler<PointerEventArgs>? OnPointerOver { get => SyncHandler<PointerEventArgs>("pointerover"); set => SetDomEventSync("pointerover", value?.Fn); }
    public HandlerAsync<PointerEventArgs>? OnPointerOverAsync { get => AsyncHandler<PointerEventArgs>("pointerover"); set => SetDomEventAsync("pointerover", value?.Fn); }

    public Handler<PointerEventArgs>? OnPointerOut { get => SyncHandler<PointerEventArgs>("pointerout"); set => SetDomEventSync("pointerout", value?.Fn); }
    public HandlerAsync<PointerEventArgs>? OnPointerOutAsync { get => AsyncHandler<PointerEventArgs>("pointerout"); set => SetDomEventAsync("pointerout", value?.Fn); }

    public Handler<PointerEventArgs>? OnPointerCancel { get => SyncHandler<PointerEventArgs>("pointercancel"); set => SetDomEventSync("pointercancel", value?.Fn); }
    public HandlerAsync<PointerEventArgs>? OnPointerCancelAsync { get => AsyncHandler<PointerEventArgs>("pointercancel"); set => SetDomEventAsync("pointercancel", value?.Fn); }

    // ---- Touch events (TouchEventArgs: active touch count + first-touch coords + modifiers) ----

    public Handler<TouchEventArgs>? OnTouchStart { get => SyncHandler<TouchEventArgs>("touchstart"); set => SetDomEventSync("touchstart", value?.Fn); }
    public HandlerAsync<TouchEventArgs>? OnTouchStartAsync { get => AsyncHandler<TouchEventArgs>("touchstart"); set => SetDomEventAsync("touchstart", value?.Fn); }

    public Handler<TouchEventArgs>? OnTouchEnd { get => SyncHandler<TouchEventArgs>("touchend"); set => SetDomEventSync("touchend", value?.Fn); }
    public HandlerAsync<TouchEventArgs>? OnTouchEndAsync { get => AsyncHandler<TouchEventArgs>("touchend"); set => SetDomEventAsync("touchend", value?.Fn); }

    public Handler<TouchEventArgs>? OnTouchMove { get => SyncHandler<TouchEventArgs>("touchmove"); set => SetDomEventSync("touchmove", value?.Fn); }
    public HandlerAsync<TouchEventArgs>? OnTouchMoveAsync { get => AsyncHandler<TouchEventArgs>("touchmove"); set => SetDomEventAsync("touchmove", value?.Fn); }

    public Handler<TouchEventArgs>? OnTouchCancel { get => SyncHandler<TouchEventArgs>("touchcancel"); set => SetDomEventSync("touchcancel", value?.Fn); }
    public HandlerAsync<TouchEventArgs>? OnTouchCancelAsync { get => AsyncHandler<TouchEventArgs>("touchcancel"); set => SetDomEventAsync("touchcancel", value?.Fn); }

    // ---- Focus events (parameterless; focus/blur reach Element via capture-phase delegation) ----

    public Handler? OnFocus { get => SyncHandler("focus"); set => SetDomEventSync("focus", value?.Fn); }
    public HandlerAsync? OnFocusAsync { get => AsyncHandler("focus"); set => SetDomEventAsync("focus", value?.Fn); }

    public Handler? OnBlur { get => SyncHandler("blur"); set => SetDomEventSync("blur", value?.Fn); }
    public HandlerAsync? OnBlurAsync { get => AsyncHandler("blur"); set => SetDomEventAsync("blur", value?.Fn); }

    public Handler? OnFocusIn { get => SyncHandler("focusin"); set => SetDomEventSync("focusin", value?.Fn); }
    public HandlerAsync? OnFocusInAsync { get => AsyncHandler("focusin"); set => SetDomEventAsync("focusin", value?.Fn); }

    public Handler? OnFocusOut { get => SyncHandler("focusout"); set => SetDomEventSync("focusout", value?.Fn); }
    public HandlerAsync? OnFocusOutAsync { get => AsyncHandler("focusout"); set => SetDomEventAsync("focusout", value?.Fn); }

    // ---- Drag events that complete the set (dragstart/over/drop/end already exist on Element) ----

    public Handler? OnDrag { get => SyncHandler("drag"); set => SetDomEventSync("drag", value?.Fn); }
    public HandlerAsync? OnDragAsync { get => AsyncHandler("drag"); set => SetDomEventAsync("drag", value?.Fn); }

    public Handler? OnDragEnter { get => SyncHandler("dragenter"); set => SetDomEventSync("dragenter", value?.Fn); }
    public HandlerAsync? OnDragEnterAsync { get => AsyncHandler("dragenter"); set => SetDomEventAsync("dragenter", value?.Fn); }

    public Handler? OnDragLeave { get => SyncHandler("dragleave"); set => SetDomEventSync("dragleave", value?.Fn); }
    public HandlerAsync? OnDragLeaveAsync { get => AsyncHandler("dragleave"); set => SetDomEventAsync("dragleave", value?.Fn); }

    // ---- Clipboard events (ClipboardEventArgs: the plain-text payload read during the event) ----

    public Handler<ClipboardEventArgs>? OnCopy { get => SyncHandler<ClipboardEventArgs>("copy"); set => SetDomEventSync("copy", value?.Fn); }
    public HandlerAsync<ClipboardEventArgs>? OnCopyAsync { get => AsyncHandler<ClipboardEventArgs>("copy"); set => SetDomEventAsync("copy", value?.Fn); }

    public Handler<ClipboardEventArgs>? OnCut { get => SyncHandler<ClipboardEventArgs>("cut"); set => SetDomEventSync("cut", value?.Fn); }
    public HandlerAsync<ClipboardEventArgs>? OnCutAsync { get => AsyncHandler<ClipboardEventArgs>("cut"); set => SetDomEventAsync("cut", value?.Fn); }

    public Handler<ClipboardEventArgs>? OnPaste { get => SyncHandler<ClipboardEventArgs>("paste"); set => SetDomEventSync("paste", value?.Fn); }
    public HandlerAsync<ClipboardEventArgs>? OnPasteAsync { get => AsyncHandler<ClipboardEventArgs>("paste"); set => SetDomEventAsync("paste", value?.Fn); }

    // ---- Remaining form-ish events (beforeinput carries the inserted text; select/invalid/reset are bare) ----

    public Handler<string>? OnBeforeInput { get => SyncHandler<string>("beforeinput"); set => SetDomEventSync("beforeinput", value?.Fn); }
    public HandlerAsync<string>? OnBeforeInputAsync { get => AsyncHandler<string>("beforeinput"); set => SetDomEventAsync("beforeinput", value?.Fn); }

    public Handler? OnSelect { get => SyncHandler("select"); set => SetDomEventSync("select", value?.Fn); }
    public HandlerAsync? OnSelectAsync { get => AsyncHandler("select"); set => SetDomEventAsync("select", value?.Fn); }

    public Handler? OnInvalid { get => SyncHandler("invalid"); set => SetDomEventSync("invalid", value?.Fn); }
    public HandlerAsync? OnInvalidAsync { get => AsyncHandler("invalid"); set => SetDomEventAsync("invalid", value?.Fn); }

    public Handler? OnReset { get => SyncHandler("reset"); set => SetDomEventSync("reset", value?.Fn); }
    public HandlerAsync? OnResetAsync { get => AsyncHandler("reset"); set => SetDomEventAsync("reset", value?.Fn); }

    // ---- Scroll (ScrollEvent: scrollTop/clientHeight/scrollHeight; rAF-coalesced client-side) ----

    public Handler<ScrollEvent>? OnScroll { get => SyncHandler<ScrollEvent>("scroll"); set => SetDomEventSync("scroll", value?.Fn); }
    public HandlerAsync<ScrollEvent>? OnScrollAsync { get => AsyncHandler<ScrollEvent>("scroll"); set => SetDomEventAsync("scroll", value?.Fn); }

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
