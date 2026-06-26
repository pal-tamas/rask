using System.Text;
using Rask.Core.Live;

namespace Rask.Core;

// The extended GlobalEventHandlers surface, mirroring the DOM mixin every HTMLElement implements: the
// `on*` handlers live on Element so EVERY tag gets them (Span(OnMouseEnter: …), Li(OnContextMenu: …)),
// not just a hand-picked few. Each event is a sync `OnXxx` (Callback / Callback<TArgs>) + async
// `OnXxxAsync` (CallbackAsync / CallbackAsync<TArgs>) pair coalesced over ONE slot in the shared
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

    public Callback? OnDragStart { get => GetDomEvent("dragstart") as Callback; set => SetDomEventSync("dragstart", value); }
    public CallbackAsync? OnDragStartAsync { get => GetDomEvent("dragstart") as CallbackAsync; set => SetDomEventAsync("dragstart", value); }

    public Callback? OnDragOver { get => GetDomEvent("dragover") as Callback; set => SetDomEventSync("dragover", value); }
    public CallbackAsync? OnDragOverAsync { get => GetDomEvent("dragover") as CallbackAsync; set => SetDomEventAsync("dragover", value); }

    public Callback? OnDrop { get => GetDomEvent("drop") as Callback; set => SetDomEventSync("drop", value); }
    public CallbackAsync? OnDropAsync { get => GetDomEvent("drop") as CallbackAsync; set => SetDomEventAsync("drop", value); }

    public Callback? OnDragEnd { get => GetDomEvent("dragend") as Callback; set => SetDomEventSync("dragend", value); }
    public CallbackAsync? OnDragEndAsync { get => GetDomEvent("dragend") as CallbackAsync; set => SetDomEventAsync("dragend", value); }

    // ---- Keyboard (KeyboardEventArgs: key/code/modifiers/repeat; the client never preventDefaults) ----

    public Callback<KeyboardEventArgs>? OnKeyDown { get => GetDomEvent("keydown") as Callback<KeyboardEventArgs>; set => SetDomEventSync("keydown", value); }
    public CallbackAsync<KeyboardEventArgs>? OnKeyDownAsync { get => GetDomEvent("keydown") as CallbackAsync<KeyboardEventArgs>; set => SetDomEventAsync("keydown", value); }

    public Callback<KeyboardEventArgs>? OnKeyUp { get => GetDomEvent("keyup") as Callback<KeyboardEventArgs>; set => SetDomEventSync("keyup", value); }
    public CallbackAsync<KeyboardEventArgs>? OnKeyUpAsync { get => GetDomEvent("keyup") as CallbackAsync<KeyboardEventArgs>; set => SetDomEventAsync("keyup", value); }

    // ---- Mouse events (MouseEventArgs: button/buttons, client/screen/page/offset/movement coords, modifiers) ----

    /// <summary>Click. Parameterless (modifier/coordinate-free) for source compatibility — use the mouse
    /// events below for geometry. The client still <c>preventDefault</c>s anchor navigation on click.</summary>
    public Callback? OnClick { get => GetDomEvent("click") as Callback; set => SetDomEventSync("click", value); }
    public CallbackAsync? OnClickAsync { get => GetDomEvent("click") as CallbackAsync; set => SetDomEventAsync("click", value); }

    public Callback<MouseEventArgs>? OnDoubleClick { get => GetDomEvent("dblclick") as Callback<MouseEventArgs>; set => SetDomEventSync("dblclick", value); }
    public CallbackAsync<MouseEventArgs>? OnDoubleClickAsync { get => GetDomEvent("dblclick") as CallbackAsync<MouseEventArgs>; set => SetDomEventAsync("dblclick", value); }

    public Callback<MouseEventArgs>? OnMouseDown { get => GetDomEvent("mousedown") as Callback<MouseEventArgs>; set => SetDomEventSync("mousedown", value); }
    public CallbackAsync<MouseEventArgs>? OnMouseDownAsync { get => GetDomEvent("mousedown") as CallbackAsync<MouseEventArgs>; set => SetDomEventAsync("mousedown", value); }

    public Callback<MouseEventArgs>? OnMouseUp { get => GetDomEvent("mouseup") as Callback<MouseEventArgs>; set => SetDomEventSync("mouseup", value); }
    public CallbackAsync<MouseEventArgs>? OnMouseUpAsync { get => GetDomEvent("mouseup") as CallbackAsync<MouseEventArgs>; set => SetDomEventAsync("mouseup", value); }

    public Callback<MouseEventArgs>? OnMouseMove { get => GetDomEvent("mousemove") as Callback<MouseEventArgs>; set => SetDomEventSync("mousemove", value); }
    public CallbackAsync<MouseEventArgs>? OnMouseMoveAsync { get => GetDomEvent("mousemove") as CallbackAsync<MouseEventArgs>; set => SetDomEventAsync("mousemove", value); }

    /// <summary>Pointer entered this element (does not fire for descendants). Simulated client-side via
    /// <c>mouseover</c> + relatedTarget boundary, since <c>mouseenter</c> itself does not delegate.</summary>
    public Callback<MouseEventArgs>? OnMouseEnter { get => GetDomEvent("mouseenter") as Callback<MouseEventArgs>; set => SetDomEventSync("mouseenter", value); }
    public CallbackAsync<MouseEventArgs>? OnMouseEnterAsync { get => GetDomEvent("mouseenter") as CallbackAsync<MouseEventArgs>; set => SetDomEventAsync("mouseenter", value); }

    public Callback<MouseEventArgs>? OnMouseLeave { get => GetDomEvent("mouseleave") as Callback<MouseEventArgs>; set => SetDomEventSync("mouseleave", value); }
    public CallbackAsync<MouseEventArgs>? OnMouseLeaveAsync { get => GetDomEvent("mouseleave") as CallbackAsync<MouseEventArgs>; set => SetDomEventAsync("mouseleave", value); }

    public Callback<MouseEventArgs>? OnMouseOver { get => GetDomEvent("mouseover") as Callback<MouseEventArgs>; set => SetDomEventSync("mouseover", value); }
    public CallbackAsync<MouseEventArgs>? OnMouseOverAsync { get => GetDomEvent("mouseover") as CallbackAsync<MouseEventArgs>; set => SetDomEventAsync("mouseover", value); }

    public Callback<MouseEventArgs>? OnMouseOut { get => GetDomEvent("mouseout") as Callback<MouseEventArgs>; set => SetDomEventSync("mouseout", value); }
    public CallbackAsync<MouseEventArgs>? OnMouseOutAsync { get => GetDomEvent("mouseout") as CallbackAsync<MouseEventArgs>; set => SetDomEventAsync("mouseout", value); }

    /// <summary>Right-click / context menu. The client <c>preventDefault</c>s so the browser menu is
    /// suppressed when you handle it.</summary>
    public Callback<MouseEventArgs>? OnContextMenu { get => GetDomEvent("contextmenu") as Callback<MouseEventArgs>; set => SetDomEventSync("contextmenu", value); }
    public CallbackAsync<MouseEventArgs>? OnContextMenuAsync { get => GetDomEvent("contextmenu") as CallbackAsync<MouseEventArgs>; set => SetDomEventAsync("contextmenu", value); }

    // ---- Wheel ----

    public Callback<WheelEventArgs>? OnWheel { get => GetDomEvent("wheel") as Callback<WheelEventArgs>; set => SetDomEventSync("wheel", value); }
    public CallbackAsync<WheelEventArgs>? OnWheelAsync { get => GetDomEvent("wheel") as CallbackAsync<WheelEventArgs>; set => SetDomEventAsync("wheel", value); }

    // ---- Pointer events (PointerEventArgs: mouse geometry + pointerId/pressure/tilt/pointerType/isPrimary) ----

    public Callback<PointerEventArgs>? OnPointerDown { get => GetDomEvent("pointerdown") as Callback<PointerEventArgs>; set => SetDomEventSync("pointerdown", value); }
    public CallbackAsync<PointerEventArgs>? OnPointerDownAsync { get => GetDomEvent("pointerdown") as CallbackAsync<PointerEventArgs>; set => SetDomEventAsync("pointerdown", value); }

    public Callback<PointerEventArgs>? OnPointerUp { get => GetDomEvent("pointerup") as Callback<PointerEventArgs>; set => SetDomEventSync("pointerup", value); }
    public CallbackAsync<PointerEventArgs>? OnPointerUpAsync { get => GetDomEvent("pointerup") as CallbackAsync<PointerEventArgs>; set => SetDomEventAsync("pointerup", value); }

    public Callback<PointerEventArgs>? OnPointerMove { get => GetDomEvent("pointermove") as Callback<PointerEventArgs>; set => SetDomEventSync("pointermove", value); }
    public CallbackAsync<PointerEventArgs>? OnPointerMoveAsync { get => GetDomEvent("pointermove") as CallbackAsync<PointerEventArgs>; set => SetDomEventAsync("pointermove", value); }

    public Callback<PointerEventArgs>? OnPointerEnter { get => GetDomEvent("pointerenter") as Callback<PointerEventArgs>; set => SetDomEventSync("pointerenter", value); }
    public CallbackAsync<PointerEventArgs>? OnPointerEnterAsync { get => GetDomEvent("pointerenter") as CallbackAsync<PointerEventArgs>; set => SetDomEventAsync("pointerenter", value); }

    public Callback<PointerEventArgs>? OnPointerLeave { get => GetDomEvent("pointerleave") as Callback<PointerEventArgs>; set => SetDomEventSync("pointerleave", value); }
    public CallbackAsync<PointerEventArgs>? OnPointerLeaveAsync { get => GetDomEvent("pointerleave") as CallbackAsync<PointerEventArgs>; set => SetDomEventAsync("pointerleave", value); }

    public Callback<PointerEventArgs>? OnPointerOver { get => GetDomEvent("pointerover") as Callback<PointerEventArgs>; set => SetDomEventSync("pointerover", value); }
    public CallbackAsync<PointerEventArgs>? OnPointerOverAsync { get => GetDomEvent("pointerover") as CallbackAsync<PointerEventArgs>; set => SetDomEventAsync("pointerover", value); }

    public Callback<PointerEventArgs>? OnPointerOut { get => GetDomEvent("pointerout") as Callback<PointerEventArgs>; set => SetDomEventSync("pointerout", value); }
    public CallbackAsync<PointerEventArgs>? OnPointerOutAsync { get => GetDomEvent("pointerout") as CallbackAsync<PointerEventArgs>; set => SetDomEventAsync("pointerout", value); }

    public Callback<PointerEventArgs>? OnPointerCancel { get => GetDomEvent("pointercancel") as Callback<PointerEventArgs>; set => SetDomEventSync("pointercancel", value); }
    public CallbackAsync<PointerEventArgs>? OnPointerCancelAsync { get => GetDomEvent("pointercancel") as CallbackAsync<PointerEventArgs>; set => SetDomEventAsync("pointercancel", value); }

    // ---- Touch events (TouchEventArgs: active touch count + first-touch coords + modifiers) ----

    public Callback<TouchEventArgs>? OnTouchStart { get => GetDomEvent("touchstart") as Callback<TouchEventArgs>; set => SetDomEventSync("touchstart", value); }
    public CallbackAsync<TouchEventArgs>? OnTouchStartAsync { get => GetDomEvent("touchstart") as CallbackAsync<TouchEventArgs>; set => SetDomEventAsync("touchstart", value); }

    public Callback<TouchEventArgs>? OnTouchEnd { get => GetDomEvent("touchend") as Callback<TouchEventArgs>; set => SetDomEventSync("touchend", value); }
    public CallbackAsync<TouchEventArgs>? OnTouchEndAsync { get => GetDomEvent("touchend") as CallbackAsync<TouchEventArgs>; set => SetDomEventAsync("touchend", value); }

    public Callback<TouchEventArgs>? OnTouchMove { get => GetDomEvent("touchmove") as Callback<TouchEventArgs>; set => SetDomEventSync("touchmove", value); }
    public CallbackAsync<TouchEventArgs>? OnTouchMoveAsync { get => GetDomEvent("touchmove") as CallbackAsync<TouchEventArgs>; set => SetDomEventAsync("touchmove", value); }

    public Callback<TouchEventArgs>? OnTouchCancel { get => GetDomEvent("touchcancel") as Callback<TouchEventArgs>; set => SetDomEventSync("touchcancel", value); }
    public CallbackAsync<TouchEventArgs>? OnTouchCancelAsync { get => GetDomEvent("touchcancel") as CallbackAsync<TouchEventArgs>; set => SetDomEventAsync("touchcancel", value); }

    // ---- Focus events (parameterless; focus/blur reach Element via capture-phase delegation) ----

    public Callback? OnFocus { get => GetDomEvent("focus") as Callback; set => SetDomEventSync("focus", value); }
    public CallbackAsync? OnFocusAsync { get => GetDomEvent("focus") as CallbackAsync; set => SetDomEventAsync("focus", value); }

    public Callback? OnBlur { get => GetDomEvent("blur") as Callback; set => SetDomEventSync("blur", value); }
    public CallbackAsync? OnBlurAsync { get => GetDomEvent("blur") as CallbackAsync; set => SetDomEventAsync("blur", value); }

    public Callback? OnFocusIn { get => GetDomEvent("focusin") as Callback; set => SetDomEventSync("focusin", value); }
    public CallbackAsync? OnFocusInAsync { get => GetDomEvent("focusin") as CallbackAsync; set => SetDomEventAsync("focusin", value); }

    public Callback? OnFocusOut { get => GetDomEvent("focusout") as Callback; set => SetDomEventSync("focusout", value); }
    public CallbackAsync? OnFocusOutAsync { get => GetDomEvent("focusout") as CallbackAsync; set => SetDomEventAsync("focusout", value); }

    // ---- Drag events that complete the set (dragstart/over/drop/end already exist on Element) ----

    public Callback? OnDrag { get => GetDomEvent("drag") as Callback; set => SetDomEventSync("drag", value); }
    public CallbackAsync? OnDragAsync { get => GetDomEvent("drag") as CallbackAsync; set => SetDomEventAsync("drag", value); }

    public Callback? OnDragEnter { get => GetDomEvent("dragenter") as Callback; set => SetDomEventSync("dragenter", value); }
    public CallbackAsync? OnDragEnterAsync { get => GetDomEvent("dragenter") as CallbackAsync; set => SetDomEventAsync("dragenter", value); }

    public Callback? OnDragLeave { get => GetDomEvent("dragleave") as Callback; set => SetDomEventSync("dragleave", value); }
    public CallbackAsync? OnDragLeaveAsync { get => GetDomEvent("dragleave") as CallbackAsync; set => SetDomEventAsync("dragleave", value); }

    // ---- Clipboard events (ClipboardEventArgs: the plain-text payload read during the event) ----

    public Callback<ClipboardEventArgs>? OnCopy { get => GetDomEvent("copy") as Callback<ClipboardEventArgs>; set => SetDomEventSync("copy", value); }
    public CallbackAsync<ClipboardEventArgs>? OnCopyAsync { get => GetDomEvent("copy") as CallbackAsync<ClipboardEventArgs>; set => SetDomEventAsync("copy", value); }

    public Callback<ClipboardEventArgs>? OnCut { get => GetDomEvent("cut") as Callback<ClipboardEventArgs>; set => SetDomEventSync("cut", value); }
    public CallbackAsync<ClipboardEventArgs>? OnCutAsync { get => GetDomEvent("cut") as CallbackAsync<ClipboardEventArgs>; set => SetDomEventAsync("cut", value); }

    public Callback<ClipboardEventArgs>? OnPaste { get => GetDomEvent("paste") as Callback<ClipboardEventArgs>; set => SetDomEventSync("paste", value); }
    public CallbackAsync<ClipboardEventArgs>? OnPasteAsync { get => GetDomEvent("paste") as CallbackAsync<ClipboardEventArgs>; set => SetDomEventAsync("paste", value); }

    // ---- Remaining form-ish events (beforeinput carries the inserted text; select/invalid/reset are bare) ----

    public Callback<string>? OnBeforeInput { get => GetDomEvent("beforeinput") as Callback<string>; set => SetDomEventSync("beforeinput", value); }
    public CallbackAsync<string>? OnBeforeInputAsync { get => GetDomEvent("beforeinput") as CallbackAsync<string>; set => SetDomEventAsync("beforeinput", value); }

    public Callback? OnSelect { get => GetDomEvent("select") as Callback; set => SetDomEventSync("select", value); }
    public CallbackAsync? OnSelectAsync { get => GetDomEvent("select") as CallbackAsync; set => SetDomEventAsync("select", value); }

    public Callback? OnInvalid { get => GetDomEvent("invalid") as Callback; set => SetDomEventSync("invalid", value); }
    public CallbackAsync? OnInvalidAsync { get => GetDomEvent("invalid") as CallbackAsync; set => SetDomEventAsync("invalid", value); }

    public Callback? OnReset { get => GetDomEvent("reset") as Callback; set => SetDomEventSync("reset", value); }
    public CallbackAsync? OnResetAsync { get => GetDomEvent("reset") as CallbackAsync; set => SetDomEventAsync("reset", value); }

    // ---- Scroll (ScrollEvent: scrollTop/clientHeight/scrollHeight; rAF-coalesced client-side) ----

    public Callback<ScrollEvent>? OnScroll { get => GetDomEvent("scroll") as Callback<ScrollEvent>; set => SetDomEventSync("scroll", value); }
    public CallbackAsync<ScrollEvent>? OnScrollAsync { get => GetDomEvent("scroll") as CallbackAsync<ScrollEvent>; set => SetDomEventAsync("scroll", value); }

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
