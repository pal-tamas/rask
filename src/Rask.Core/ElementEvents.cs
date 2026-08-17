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

    /// <summary>
    ///     The user started dragging this element. Put the payload on the drag data here — a drag that carries
    ///     nothing drops nothing. The element must also set <c>Draggable</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragstart_event">MDN</see>
    /// </summary>
    public Action? OnDragStart { get => SyncHandler("dragstart"); set => SetDomEventSync("dragstart", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnDragStart"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnDragStartAsync { get => AsyncHandler("dragstart"); set => SetDomEventAsync("dragstart", value); }

    /// <summary>
    ///     Fires continuously while a dragged item is over this element. The default has to be prevented on
    ///     <em>every</em> one of them, not just the first, or the drop never happens.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragover_event">MDN</see>
    /// </summary>
    public Action? OnDragOver { get => SyncHandler("dragover"); set => SetDomEventSync("dragover", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnDragOver"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnDragOverAsync { get => AsyncHandler("dragover"); set => SetDomEventAsync("dragover", value); }

    /// <summary>
    ///     A dragged item was released on this element. Read the transferred data here — and only reached if the
    ///     <c>dragover</c> default was prevented.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/drop_event">MDN</see>
    /// </summary>
    public Action? OnDrop { get => SyncHandler("drop"); set => SetDomEventSync("drop", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnDrop"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnDropAsync { get => AsyncHandler("drop"); set => SetDomEventAsync("drop", value); }

    /// <summary>
    ///     The drag finished — dropped or cancelled, this fires either way, on the element the drag started from.
    ///     The place to clear drag state, since a cancelled drag reaches no drop handler.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragend_event">MDN</see>
    /// </summary>
    public Action? OnDragEnd { get => SyncHandler("dragend"); set => SetDomEventSync("dragend", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnDragEnd"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnDragEndAsync { get => AsyncHandler("dragend"); set => SetDomEventAsync("dragend", value); }

    // ---- Keyboard (KeyboardEventArgs: key/code/modifiers/repeat; the client never preventDefaults) ----

    /// <summary>
    ///     A key went down, and keeps firing while it is held. The event to use for shortcuts and for keys with a
    ///     default worth cancelling — Escape to close, Enter to submit, arrows to move a selection.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/keydown_event">MDN</see>
    /// </summary>
    public Action<KeyboardEventArgs>? OnKeyDown { get => SyncHandler<KeyboardEventArgs>("keydown"); set => SetDomEventSync("keydown", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnKeyDown"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<KeyboardEventArgs, Task>? OnKeyDownAsync { get => AsyncHandler<KeyboardEventArgs>("keydown"); set => SetDomEventAsync("keydown", value); }

    /// <summary>
    ///     A key was released. Not the one to use for shortcuts: a held key does not reach it until the user lets
    ///     go, so the response feels late.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/keyup_event">MDN</see>
    /// </summary>
    public Action<KeyboardEventArgs>? OnKeyUp { get => SyncHandler<KeyboardEventArgs>("keyup"); set => SetDomEventSync("keyup", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnKeyUp"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<KeyboardEventArgs, Task>? OnKeyUpAsync { get => AsyncHandler<KeyboardEventArgs>("keyup"); set => SetDomEventAsync("keyup", value); }

    // ---- Mouse events (MouseEventArgs: button/buttons, client/screen/page/offset/movement coords, modifiers) ----

    /// <summary>Click. Parameterless (modifier/coordinate-free) for source compatibility — use the mouse
    /// events below for geometry. The client still <c>preventDefault</c>s anchor navigation on click.</summary>
    public Action? OnClick { get => SyncHandler("click"); set => SetDomEventSync("click", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnClick"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnClickAsync { get => AsyncHandler("click"); set => SetDomEventAsync("click", value); }

    /// <summary>
    ///     The element was double-clicked. A click handler still fires — twice — before this does, so the two must
    ///     not both act, or the single-click action runs on the way to the double-click one.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/dblclick_event">MDN</see>
    /// </summary>
    public Action<MouseEventArgs>? OnDoubleClick { get => SyncHandler<MouseEventArgs>("dblclick"); set => SetDomEventSync("dblclick", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnDoubleClick"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MouseEventArgs, Task>? OnDoubleClickAsync { get => AsyncHandler<MouseEventArgs>("dblclick"); set => SetDomEventAsync("dblclick", value); }

    /// <summary>
    ///     A mouse button went down over this element. Fires before any click, and is what a drag or press-and-hold
    ///     gesture starts from.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mousedown_event">MDN</see>
    /// </summary>
    public Action<MouseEventArgs>? OnMouseDown { get => SyncHandler<MouseEventArgs>("mousedown"); set => SetDomEventSync("mousedown", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnMouseDown"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MouseEventArgs, Task>? OnMouseDownAsync { get => AsyncHandler<MouseEventArgs>("mousedown"); set => SetDomEventAsync("mousedown", value); }

    /// <summary>
    ///     A mouse button was released over this element. A click only follows if the matching <c>mousedown</c>
    ///     happened on this same element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mouseup_event">MDN</see>
    /// </summary>
    public Action<MouseEventArgs>? OnMouseUp { get => SyncHandler<MouseEventArgs>("mouseup"); set => SetDomEventSync("mouseup", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnMouseUp"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MouseEventArgs, Task>? OnMouseUpAsync { get => AsyncHandler<MouseEventArgs>("mouseup"); set => SetDomEventAsync("mouseup", value); }

    /// <summary>
    ///     The pointer moved over this element. Fires at pointer rate — do no layout reads here without throttling,
    ///     or scrolling stutters.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mousemove_event">MDN</see>
    /// </summary>
    public Action<MouseEventArgs>? OnMouseMove { get => SyncHandler<MouseEventArgs>("mousemove"); set => SetDomEventSync("mousemove", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnMouseMove"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MouseEventArgs, Task>? OnMouseMoveAsync { get => AsyncHandler<MouseEventArgs>("mousemove"); set => SetDomEventAsync("mousemove", value); }

    /// <summary>Pointer entered this element (does not fire for descendants). Simulated client-side via
    /// <c>mouseover</c> + relatedTarget boundary, since <c>mouseenter</c> itself does not delegate.</summary>
    public Action<MouseEventArgs>? OnMouseEnter { get => SyncHandler<MouseEventArgs>("mouseenter"); set => SetDomEventSync("mouseenter", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnMouseEnter"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MouseEventArgs, Task>? OnMouseEnterAsync { get => AsyncHandler<MouseEventArgs>("mouseenter"); set => SetDomEventAsync("mouseenter", value); }

    /// <summary>
    ///     The pointer left this element. Does not bubble and does not fire for descendants, so it pairs cleanly
    ///     with <c>mouseenter</c> for hover state.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mouseleave_event">MDN</see>
    /// </summary>
    public Action<MouseEventArgs>? OnMouseLeave { get => SyncHandler<MouseEventArgs>("mouseleave"); set => SetDomEventSync("mouseleave", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnMouseLeave"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MouseEventArgs, Task>? OnMouseLeaveAsync { get => AsyncHandler<MouseEventArgs>("mouseleave"); set => SetDomEventAsync("mouseleave", value); }

    /// <summary>
    ///     The pointer entered this element <em>or any descendant</em>. It bubbles, so it fires again every time
    ///     the pointer crosses into a child — use <c>mouseenter</c> for plain hover.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mouseover_event">MDN</see>
    /// </summary>
    public Action<MouseEventArgs>? OnMouseOver { get => SyncHandler<MouseEventArgs>("mouseover"); set => SetDomEventSync("mouseover", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnMouseOver"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MouseEventArgs, Task>? OnMouseOverAsync { get => AsyncHandler<MouseEventArgs>("mouseover"); set => SetDomEventAsync("mouseover", value); }

    /// <summary>
    ///     The pointer left this element <em>or any descendant</em>. It bubbles, so moving between two children
    ///     fires it — use <c>mouseleave</c> for plain hover.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mouseout_event">MDN</see>
    /// </summary>
    public Action<MouseEventArgs>? OnMouseOut { get => SyncHandler<MouseEventArgs>("mouseout"); set => SetDomEventSync("mouseout", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnMouseOut"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MouseEventArgs, Task>? OnMouseOutAsync { get => AsyncHandler<MouseEventArgs>("mouseout"); set => SetDomEventAsync("mouseout", value); }

    /// <summary>Right-click / context menu. The client <c>preventDefault</c>s so the browser menu is
    /// suppressed when you handle it.</summary>
    public Action<MouseEventArgs>? OnContextMenu { get => SyncHandler<MouseEventArgs>("contextmenu"); set => SetDomEventSync("contextmenu", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnContextMenu"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MouseEventArgs, Task>? OnContextMenuAsync { get => AsyncHandler<MouseEventArgs>("contextmenu"); set => SetDomEventAsync("contextmenu", value); }

    // ---- Wheel ----

    /// <summary>
    ///     The wheel turned over this element. Not a scroll: the page may not move at all, and cancelling this does
    ///     not stop momentum scrolling that is already running.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/wheel_event">MDN</see>
    /// </summary>
    public Action<WheelEventArgs>? OnWheel { get => SyncHandler<WheelEventArgs>("wheel"); set => SetDomEventSync("wheel", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnWheel"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<WheelEventArgs, Task>? OnWheelAsync { get => AsyncHandler<WheelEventArgs>("wheel"); set => SetDomEventAsync("wheel", value); }

    // ---- Pointer events (PointerEventArgs: mouse geometry + pointerId/pressure/tilt/pointerType/isPrimary) ----

    /// <summary>
    ///     A pointer — mouse, pen or finger — went down on this element. Prefer the pointer events to the mouse and
    ///     touch pairs: one handler covers all three input kinds.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerdown_event">MDN</see>
    /// </summary>
    public Action<PointerEventArgs>? OnPointerDown { get => SyncHandler<PointerEventArgs>("pointerdown"); set => SetDomEventSync("pointerdown", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPointerDown"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<PointerEventArgs, Task>? OnPointerDownAsync { get => AsyncHandler<PointerEventArgs>("pointerdown"); set => SetDomEventAsync("pointerdown", value); }

    /// <summary>
    ///     A pointer was released over this element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerup_event">MDN</see>
    /// </summary>
    public Action<PointerEventArgs>? OnPointerUp { get => SyncHandler<PointerEventArgs>("pointerup"); set => SetDomEventSync("pointerup", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPointerUp"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<PointerEventArgs, Task>? OnPointerUpAsync { get => AsyncHandler<PointerEventArgs>("pointerup"); set => SetDomEventAsync("pointerup", value); }

    /// <summary>
    ///     A pointer moved over this element. Fires at pointer rate, so keep the handler cheap.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointermove_event">MDN</see>
    /// </summary>
    public Action<PointerEventArgs>? OnPointerMove { get => SyncHandler<PointerEventArgs>("pointermove"); set => SetDomEventSync("pointermove", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPointerMove"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<PointerEventArgs, Task>? OnPointerMoveAsync { get => AsyncHandler<PointerEventArgs>("pointermove"); set => SetDomEventAsync("pointermove", value); }

    /// <summary>
    ///     A pointer entered this element. Does not bubble, so descendants do not re-fire it.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerenter_event">MDN</see>
    /// </summary>
    public Action<PointerEventArgs>? OnPointerEnter { get => SyncHandler<PointerEventArgs>("pointerenter"); set => SetDomEventSync("pointerenter", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPointerEnter"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<PointerEventArgs, Task>? OnPointerEnterAsync { get => AsyncHandler<PointerEventArgs>("pointerenter"); set => SetDomEventAsync("pointerenter", value); }

    /// <summary>
    ///     A pointer left this element. Does not bubble.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerleave_event">MDN</see>
    /// </summary>
    public Action<PointerEventArgs>? OnPointerLeave { get => SyncHandler<PointerEventArgs>("pointerleave"); set => SetDomEventSync("pointerleave", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPointerLeave"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<PointerEventArgs, Task>? OnPointerLeaveAsync { get => AsyncHandler<PointerEventArgs>("pointerleave"); set => SetDomEventAsync("pointerleave", value); }

    /// <summary>
    ///     A pointer entered this element or any descendant. Bubbles, unlike <c>pointerenter</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerover_event">MDN</see>
    /// </summary>
    public Action<PointerEventArgs>? OnPointerOver { get => SyncHandler<PointerEventArgs>("pointerover"); set => SetDomEventSync("pointerover", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPointerOver"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<PointerEventArgs, Task>? OnPointerOverAsync { get => AsyncHandler<PointerEventArgs>("pointerover"); set => SetDomEventAsync("pointerover", value); }

    /// <summary>
    ///     A pointer left this element or any descendant. Bubbles, unlike <c>pointerleave</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerout_event">MDN</see>
    /// </summary>
    public Action<PointerEventArgs>? OnPointerOut { get => SyncHandler<PointerEventArgs>("pointerout"); set => SetDomEventSync("pointerout", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPointerOut"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<PointerEventArgs, Task>? OnPointerOutAsync { get => AsyncHandler<PointerEventArgs>("pointerout"); set => SetDomEventAsync("pointerout", value); }

    /// <summary>
    ///     The browser took the pointer away — a touch became a scroll, or the gesture was interrupted. Handle it
    ///     wherever you handle <c>pointerup</c>, or a cancelled gesture leaves the element stuck mid-drag.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointercancel_event">MDN</see>
    /// </summary>
    public Action<PointerEventArgs>? OnPointerCancel { get => SyncHandler<PointerEventArgs>("pointercancel"); set => SetDomEventSync("pointercancel", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPointerCancel"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<PointerEventArgs, Task>? OnPointerCancelAsync { get => AsyncHandler<PointerEventArgs>("pointercancel"); set => SetDomEventAsync("pointercancel", value); }

    // ---- Touch events (TouchEventArgs: active touch count + first-touch coords + modifiers) ----

    /// <summary>
    ///     A finger touched this element. Only reach for the touch events when you need per-finger detail; the
    ///     pointer events cover touch as well and cost one handler instead of two.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/touchstart_event">MDN</see>
    /// </summary>
    public Action<TouchEventArgs>? OnTouchStart { get => SyncHandler<TouchEventArgs>("touchstart"); set => SetDomEventSync("touchstart", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnTouchStart"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<TouchEventArgs, Task>? OnTouchStartAsync { get => AsyncHandler<TouchEventArgs>("touchstart"); set => SetDomEventAsync("touchstart", value); }

    /// <summary>
    ///     A finger left the screen.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/touchend_event">MDN</see>
    /// </summary>
    public Action<TouchEventArgs>? OnTouchEnd { get => SyncHandler<TouchEventArgs>("touchend"); set => SetDomEventSync("touchend", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnTouchEnd"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<TouchEventArgs, Task>? OnTouchEndAsync { get => AsyncHandler<TouchEventArgs>("touchend"); set => SetDomEventAsync("touchend", value); }

    /// <summary>
    ///     A finger moved across this element. Cancelling it stops the page scrolling with the finger, so cancel
    ///     only when the gesture really is yours.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/touchmove_event">MDN</see>
    /// </summary>
    public Action<TouchEventArgs>? OnTouchMove { get => SyncHandler<TouchEventArgs>("touchmove"); set => SetDomEventSync("touchmove", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnTouchMove"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<TouchEventArgs, Task>? OnTouchMoveAsync { get => AsyncHandler<TouchEventArgs>("touchmove"); set => SetDomEventAsync("touchmove", value); }

    /// <summary>
    ///     The browser took over the touch — typically because it became a scroll. Undo whatever the gesture had
    ///     started, the same way a cancelled pointer is handled.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/touchcancel_event">MDN</see>
    /// </summary>
    public Action<TouchEventArgs>? OnTouchCancel { get => SyncHandler<TouchEventArgs>("touchcancel"); set => SetDomEventSync("touchcancel", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnTouchCancel"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<TouchEventArgs, Task>? OnTouchCancelAsync { get => AsyncHandler<TouchEventArgs>("touchcancel"); set => SetDomEventAsync("touchcancel", value); }

    // ---- Focus events (parameterless; focus/blur reach Element via capture-phase delegation) ----

    /// <summary>
    ///     This element received focus. Does not bubble — to catch focus arriving anywhere inside a subtree, use
    ///     <c>focusin</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/focus_event">MDN</see>
    /// </summary>
    public Action? OnFocus { get => SyncHandler("focus"); set => SetDomEventSync("focus", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnFocus"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnFocusAsync { get => AsyncHandler("focus"); set => SetDomEventAsync("focus", value); }

    /// <summary>
    ///     This element lost focus. The natural moment to validate a field: on blur the user has finished typing,
    ///     whereas validating per keystroke shouts at them mid-word. Does not bubble — see <c>focusout</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/blur_event">MDN</see>
    /// </summary>
    public Action? OnBlur { get => SyncHandler("blur"); set => SetDomEventSync("blur", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnBlur"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnBlurAsync { get => AsyncHandler("blur"); set => SetDomEventAsync("blur", value); }

    /// <summary>
    ///     Focus arrived at this element or anything inside it. The bubbling form of <c>focus</c>, so one handler
    ///     on a container covers every control in it.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/focusin_event">MDN</see>
    /// </summary>
    public Action? OnFocusIn { get => SyncHandler("focusin"); set => SetDomEventSync("focusin", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnFocusIn"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnFocusInAsync { get => AsyncHandler("focusin"); set => SetDomEventAsync("focusin", value); }

    /// <summary>
    ///     Focus left this element or anything inside it. The bubbling form of <c>blur</c>. Careful: it fires while
    ///     moving between two children too, so a 'closed the whole group' check has to test where focus actually
    ///     went.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/focusout_event">MDN</see>
    /// </summary>
    public Action? OnFocusOut { get => SyncHandler("focusout"); set => SetDomEventSync("focusout", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnFocusOut"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnFocusOutAsync { get => AsyncHandler("focusout"); set => SetDomEventAsync("focusout", value); }

    // ---- Drag events that complete the set (dragstart/over/drop/end already exist on Element) ----

    /// <summary>
    ///     Fires continuously while this element is being dragged. It runs at pointer rate, so keep the handler
    ///     cheap and drive visuals from CSS where you can.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/drag_event">MDN</see>
    /// </summary>
    public Action? OnDrag { get => SyncHandler("drag"); set => SetDomEventSync("drag", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnDrag"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnDragAsync { get => AsyncHandler("drag"); set => SetDomEventAsync("drag", value); }

    /// <summary>
    ///     A dragged item entered this element. Cancel the event to advertise this element as a drop target — an
    ///     element that never cancels is not one.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragenter_event">MDN</see>
    /// </summary>
    public Action? OnDragEnter { get => SyncHandler("dragenter"); set => SetDomEventSync("dragenter", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnDragEnter"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnDragEnterAsync { get => AsyncHandler("dragenter"); set => SetDomEventAsync("dragenter", value); }

    /// <summary>
    ///     A dragged item left this element. Pairs with <c>dragenter</c> to undo whatever hover styling that turned
    ///     on.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragleave_event">MDN</see>
    /// </summary>
    public Action? OnDragLeave { get => SyncHandler("dragleave"); set => SetDomEventSync("dragleave", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnDragLeave"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnDragLeaveAsync { get => AsyncHandler("dragleave"); set => SetDomEventAsync("dragleave", value); }

    // ---- Clipboard events (ClipboardEventArgs: the plain-text payload read during the event) ----

    /// <summary>
    ///     The user copied from this element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/copy_event">MDN</see>
    /// </summary>
    public Action<ClipboardEventArgs>? OnCopy { get => SyncHandler<ClipboardEventArgs>("copy"); set => SetDomEventSync("copy", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnCopy"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<ClipboardEventArgs, Task>? OnCopyAsync { get => AsyncHandler<ClipboardEventArgs>("copy"); set => SetDomEventAsync("copy", value); }

    /// <summary>
    ///     The user cut from this element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/cut_event">MDN</see>
    /// </summary>
    public Action<ClipboardEventArgs>? OnCut { get => SyncHandler<ClipboardEventArgs>("cut"); set => SetDomEventSync("cut", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnCut"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<ClipboardEventArgs, Task>? OnCutAsync { get => AsyncHandler<ClipboardEventArgs>("cut"); set => SetDomEventAsync("cut", value); }

    /// <summary>
    ///     The user pasted into this element. The place to sanitise or reformat pasted content before it lands.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/paste_event">MDN</see>
    /// </summary>
    public Action<ClipboardEventArgs>? OnPaste { get => SyncHandler<ClipboardEventArgs>("paste"); set => SetDomEventSync("paste", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPaste"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<ClipboardEventArgs, Task>? OnPasteAsync { get => AsyncHandler<ClipboardEventArgs>("paste"); set => SetDomEventAsync("paste", value); }

    // ---- Remaining form-ish events (beforeinput carries the inserted text; select/invalid/reset are bare) ----

    /// <summary>
    ///     Fires before the value changes, carrying the text about to be inserted — so it is where input can be
    ///     inspected, and rejected, while the old value is still in place.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/beforeinput_event">MDN</see>
    /// </summary>
    public Action<string>? OnBeforeInput { get => SyncHandler<string>("beforeinput"); set => SetDomEventSync("beforeinput", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnBeforeInput"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<string, Task>? OnBeforeInputAsync { get => AsyncHandler<string>("beforeinput"); set => SetDomEventAsync("beforeinput", value); }

    /// <summary>
    ///     The user selected text inside this control.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/select_event">MDN</see>
    /// </summary>
    public Action? OnSelect { get => SyncHandler("select"); set => SetDomEventSync("select", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnSelect"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnSelectAsync { get => AsyncHandler("select"); set => SetDomEventAsync("select", value); }

    /// <summary>
    ///     Constraint validation failed for this control. Fires per control when a submit is blocked, which is the
    ///     hook for replacing the browser's default bubble with your own message.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLInputElement/invalid_event">MDN</see>
    /// </summary>
    public Action? OnInvalid { get => SyncHandler("invalid"); set => SetDomEventSync("invalid", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnInvalid"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnInvalidAsync { get => AsyncHandler("invalid"); set => SetDomEventAsync("invalid", value); }

    /// <summary>
    ///     The form was reset. Any state you keep outside the model has to be rolled back here too, or the visible
    ///     form and your state disagree.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLFormElement/reset_event">MDN</see>
    /// </summary>
    public Action? OnReset { get => SyncHandler("reset"); set => SetDomEventSync("reset", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnReset"/>, awaited by the renderer before it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<Task>? OnResetAsync { get => AsyncHandler("reset"); set => SetDomEventAsync("reset", value); }

    // ---- Scroll (ScrollEvent: scrollTop/clientHeight/scrollHeight; rAF-coalesced client-side) ----

    /// <summary>
    ///     This element was scrolled. Fires at scroll rate and after the fact — read positions here, never write
    ///     layout, or you get a scroll-jank feedback loop.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/scroll_event">MDN</see>
    /// </summary>
    public Action<ScrollEvent>? OnScroll { get => SyncHandler<ScrollEvent>("scroll"); set => SetDomEventSync("scroll", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnScroll"/>, awaited by the renderer before it
    ///     re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and this is
    ///         silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
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
