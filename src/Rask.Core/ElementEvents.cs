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
        "scroll",
        // Appended, so no existing attribute's position moves — the serialized order is asserted by
        // tests. Chronological within the pair, as the drag and keyboard groups above are.
        "beforetoggle", "toggle"
    };

    // Unified backing store for the WHOLE event surface — drag, keyboard, click, scroll, mouse, pointer,
    // touch, focus, clipboard, wheel, plus the HTMLMediaElement events. A single keyed dictionary instead
    // of ~50 named fields, kept as a DIRECT Element field (not hoisted into LiveState): a click-bearing
    // leaf would otherwise force a whole LiveState allocation, whereas this allocates only the small dict
    // on first handler. A plain element that wires nothing keeps `_domEvents` null and pays one extra
    // reference field. Each event name maps to ONE slot holding the delegate.
    //
    // The slot used to carry an IsAsync flag beside it, because an event was TWO properties — `OnClick`
    // and `OnClickAsync` — over this one slot, and a null re-applied by the factory had to clear only its
    // own kind. One `Callback` property per event makes both the flag and that asymmetry unnecessary: a
    // write is a write, and "both wired" is no longer expressible rather than diagnosed (RASK027).
    private Dictionary<string, Delegate>? _domEvents;

    // Render-hotpath early-out: WriteAttributes asks this before iterating the ordered event list. A
    // plain element answers false in one null check, so the per-render cost stays at zero.
    private protected bool HasDomEvents => _domEvents is { Count: > 0 };

    private protected Delegate? GetDomEvent(string name) =>
        _domEvents is { } map && map.TryGetValue(name, out var slot) ? slot : null;

    // ---- Typed views over the slot ----------------------------------------------------------------
    //
    // The dictionary holds every handler as a bare `Delegate`, because that is what dispatch needs, and
    // the properties below hand one back inside a `Callback` — a struct that HOLDS a delegate without
    // being one.
    //
    // That carrier is doing two jobs. It collapses the sync/async pair into one property, so an event has
    // one name and one slot and cannot be given two handlers. And because it is not a delegate type, it
    // cannot swallow a chain step of the same name: C# stops at a delegate-typed property when resolving
    // `x.OnClick(fn)` and reads the call as an invocation (CS1593), which is the whole reason the chain
    // had to receive one step off the component. A non-invocable member falls through to extension lookup
    // instead. See Rask.Core.Callback.
    //
    // Reading is total now rather than partial: a slot holding "the other kind" used to read back as null
    // through the `as` cast, and there is no other kind left.
    private protected Callback? Handler(string name) =>
        GetDomEvent(name) is { } d ? new Callback(d) : null;

    private protected Callback<TArgs>? Handler<TArgs>(string name) =>
        GetDomEvent(name) is { } d ? new Callback<TArgs>(d) : null;

    // One writer, and a write is simply a write. There used to be two — a sync one that always won and an
    // async one that deferred to it — because an event was two properties over this one slot and the
    // runtime needed a tiebreaker for "both wired" (RASK027 reported the same thing at compile time).
    // With one property per event that state cannot be reached, so neither the tiebreaker nor the
    // clear-only-my-own-kind rule has anything left to arbitrate.
    private protected void SetHandler(string name, Delegate? value)
    {
        if (value is not null)
        {
            (_domEvents ??= new Dictionary<string, Delegate>(StringComparer.Ordinal))[name] = value;
        }
        else
        {
            _domEvents?.Remove(name);
        }
    }

    // ---- Drag & drop (parameterless; the dragged item's identity rides the handler's closure — see
    //      the headless DragDrop primitive). dragstart/over/drop/end here; drag/dragenter/dragleave below. ----

    /// <summary>
    ///     The user started dragging this element. Put the payload on the drag data here — a drag that carries
    ///     nothing drops nothing. The element must also set <c>Draggable</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragstart_event">MDN</see>
    /// </summary>
    public Callback? OnDragStart { get => Handler("dragstart"); set => SetHandler("dragstart", value?.Handler); }

    /// <summary>
    ///     Fires continuously while a dragged item is over this element. The default has to be prevented on
    ///     <em>every</em> one of them, not just the first, or the drop never happens.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragover_event">MDN</see>
    /// </summary>
    public Callback? OnDragOver { get => Handler("dragover"); set => SetHandler("dragover", value?.Handler); }

    /// <summary>
    ///     A dragged item was released on this element. Read the transferred data here — and only reached if the
    ///     <c>dragover</c> default was prevented.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/drop_event">MDN</see>
    /// </summary>
    public Callback? OnDrop { get => Handler("drop"); set => SetHandler("drop", value?.Handler); }

    /// <summary>
    ///     The drag finished — dropped or cancelled, this fires either way, on the element the drag started from.
    ///     The place to clear drag state, since a cancelled drag reaches no drop handler.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragend_event">MDN</see>
    /// </summary>
    public Callback? OnDragEnd { get => Handler("dragend"); set => SetHandler("dragend", value?.Handler); }

    // ---- Keyboard (KeyboardEventArgs: key/code/modifiers/repeat; the client never preventDefaults) ----

    /// <summary>
    ///     A key went down, and keeps firing while it is held. The event to use for shortcuts and for keys with a
    ///     default worth cancelling — Escape to close, Enter to submit, arrows to move a selection.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/keydown_event">MDN</see>
    /// </summary>
    public Callback<KeyboardEventArgs>? OnKeyDown { get => Handler<KeyboardEventArgs>("keydown"); set => SetHandler("keydown", value?.Handler); }

    /// <summary>
    ///     A key was released. Not the one to use for shortcuts: a held key does not reach it until the user lets
    ///     go, so the response feels late.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/keyup_event">MDN</see>
    /// </summary>
    public Callback<KeyboardEventArgs>? OnKeyUp { get => Handler<KeyboardEventArgs>("keyup"); set => SetHandler("keyup", value?.Handler); }

    // ---- Open state (ToggleEventArgs: the platform's oldState/newState) ----

    /// <summary>
    ///     A popover or <c>&lt;details&gt;</c> finished opening or closing.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/toggle_event">MDN</see>
    ///     <para>
    ///         The event that closes the gap between the browser owning dismissal and C# owning the state. A
    ///         <c>[popover]</c> closes itself on Escape and on a click outside, and without this nothing says
    ///         so — a component tracking its own open flag goes on believing the panel is open, and its
    ///         <c>aria-expanded</c> goes on saying so over a closed panel.
    ///     </para>
    /// </summary>
    public Callback<ToggleEventArgs>? OnToggle { get => Handler<ToggleEventArgs>("toggle"); set => SetHandler("toggle", value?.Handler); }

    /// <summary>
    ///     The same transition, just before it happens. Use it to prepare what is about to be shown — loading
    ///     a panel's contents as it opens — rather than to react to what already did.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/beforetoggle_event">MDN</see>
    ///     <para>
    ///         It cannot cancel the transition: the client never <c>preventDefault</c>s, so this is a
    ///         notification and not a veto.
    ///     </para>
    /// </summary>
    public Callback<ToggleEventArgs>? OnBeforeToggle { get => Handler<ToggleEventArgs>("beforetoggle"); set => SetHandler("beforetoggle", value?.Handler); }

    // ---- Mouse events (MouseEventArgs: button/buttons, client/screen/page/offset/movement coords, modifiers) ----

    /// <summary>Click. Parameterless (modifier/coordinate-free) for source compatibility — use the mouse
    /// events below for geometry. The client still <c>preventDefault</c>s anchor navigation on click.</summary>
    public Callback? OnClick { get => Handler("click"); set => SetHandler("click", value?.Handler); }

    /// <summary>
    ///     The element was double-clicked. A click handler still fires — twice — before this does, so the two must
    ///     not both act, or the single-click action runs on the way to the double-click one.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/dblclick_event">MDN</see>
    /// </summary>
    public Callback<MouseEventArgs>? OnDoubleClick { get => Handler<MouseEventArgs>("dblclick"); set => SetHandler("dblclick", value?.Handler); }

    /// <summary>
    ///     A mouse button went down over this element. Fires before any click, and is what a drag or press-and-hold
    ///     gesture starts from.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mousedown_event">MDN</see>
    /// </summary>
    public Callback<MouseEventArgs>? OnMouseDown { get => Handler<MouseEventArgs>("mousedown"); set => SetHandler("mousedown", value?.Handler); }

    /// <summary>
    ///     A mouse button was released over this element. A click only follows if the matching <c>mousedown</c>
    ///     happened on this same element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mouseup_event">MDN</see>
    /// </summary>
    public Callback<MouseEventArgs>? OnMouseUp { get => Handler<MouseEventArgs>("mouseup"); set => SetHandler("mouseup", value?.Handler); }

    /// <summary>
    ///     The pointer moved over this element. Fires at pointer rate — do no layout reads here without throttling,
    ///     or scrolling stutters.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mousemove_event">MDN</see>
    /// </summary>
    public Callback<MouseEventArgs>? OnMouseMove { get => Handler<MouseEventArgs>("mousemove"); set => SetHandler("mousemove", value?.Handler); }

    /// <summary>Pointer entered this element (does not fire for descendants). Simulated client-side via
    /// <c>mouseover</c> + relatedTarget boundary, since <c>mouseenter</c> itself does not delegate.</summary>
    public Callback<MouseEventArgs>? OnMouseEnter { get => Handler<MouseEventArgs>("mouseenter"); set => SetHandler("mouseenter", value?.Handler); }

    /// <summary>
    ///     The pointer left this element. Does not bubble and does not fire for descendants, so it pairs cleanly
    ///     with <c>mouseenter</c> for hover state.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mouseleave_event">MDN</see>
    /// </summary>
    public Callback<MouseEventArgs>? OnMouseLeave { get => Handler<MouseEventArgs>("mouseleave"); set => SetHandler("mouseleave", value?.Handler); }

    /// <summary>
    ///     The pointer entered this element <em>or any descendant</em>. It bubbles, so it fires again every time
    ///     the pointer crosses into a child — use <c>mouseenter</c> for plain hover.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mouseover_event">MDN</see>
    /// </summary>
    public Callback<MouseEventArgs>? OnMouseOver { get => Handler<MouseEventArgs>("mouseover"); set => SetHandler("mouseover", value?.Handler); }

    /// <summary>
    ///     The pointer left this element <em>or any descendant</em>. It bubbles, so moving between two children
    ///     fires it — use <c>mouseleave</c> for plain hover.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/mouseout_event">MDN</see>
    /// </summary>
    public Callback<MouseEventArgs>? OnMouseOut { get => Handler<MouseEventArgs>("mouseout"); set => SetHandler("mouseout", value?.Handler); }

    /// <summary>Right-click / context menu. The client <c>preventDefault</c>s so the browser menu is
    /// suppressed when you handle it.</summary>
    public Callback<MouseEventArgs>? OnContextMenu { get => Handler<MouseEventArgs>("contextmenu"); set => SetHandler("contextmenu", value?.Handler); }

    // ---- Wheel ----

    /// <summary>
    ///     The wheel turned over this element. Not a scroll: the page may not move at all, and cancelling this does
    ///     not stop momentum scrolling that is already running.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/wheel_event">MDN</see>
    /// </summary>
    public Callback<WheelEventArgs>? OnWheel { get => Handler<WheelEventArgs>("wheel"); set => SetHandler("wheel", value?.Handler); }

    // ---- Pointer events (PointerEventArgs: mouse geometry + pointerId/pressure/tilt/pointerType/isPrimary) ----

    /// <summary>
    ///     A pointer — mouse, pen or finger — went down on this element. Prefer the pointer events to the mouse and
    ///     touch pairs: one handler covers all three input kinds.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerdown_event">MDN</see>
    /// </summary>
    public Callback<PointerEventArgs>? OnPointerDown { get => Handler<PointerEventArgs>("pointerdown"); set => SetHandler("pointerdown", value?.Handler); }

    /// <summary>
    ///     A pointer was released over this element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerup_event">MDN</see>
    /// </summary>
    public Callback<PointerEventArgs>? OnPointerUp { get => Handler<PointerEventArgs>("pointerup"); set => SetHandler("pointerup", value?.Handler); }

    /// <summary>
    ///     A pointer moved over this element. Fires at pointer rate, so keep the handler cheap.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointermove_event">MDN</see>
    /// </summary>
    public Callback<PointerEventArgs>? OnPointerMove { get => Handler<PointerEventArgs>("pointermove"); set => SetHandler("pointermove", value?.Handler); }

    /// <summary>
    ///     A pointer entered this element. Does not bubble, so descendants do not re-fire it.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerenter_event">MDN</see>
    /// </summary>
    public Callback<PointerEventArgs>? OnPointerEnter { get => Handler<PointerEventArgs>("pointerenter"); set => SetHandler("pointerenter", value?.Handler); }

    /// <summary>
    ///     A pointer left this element. Does not bubble.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerleave_event">MDN</see>
    /// </summary>
    public Callback<PointerEventArgs>? OnPointerLeave { get => Handler<PointerEventArgs>("pointerleave"); set => SetHandler("pointerleave", value?.Handler); }

    /// <summary>
    ///     A pointer entered this element or any descendant. Bubbles, unlike <c>pointerenter</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerover_event">MDN</see>
    /// </summary>
    public Callback<PointerEventArgs>? OnPointerOver { get => Handler<PointerEventArgs>("pointerover"); set => SetHandler("pointerover", value?.Handler); }

    /// <summary>
    ///     A pointer left this element or any descendant. Bubbles, unlike <c>pointerleave</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointerout_event">MDN</see>
    /// </summary>
    public Callback<PointerEventArgs>? OnPointerOut { get => Handler<PointerEventArgs>("pointerout"); set => SetHandler("pointerout", value?.Handler); }

    /// <summary>
    ///     The browser took the pointer away — a touch became a scroll, or the gesture was interrupted. Handle it
    ///     wherever you handle <c>pointerup</c>, or a cancelled gesture leaves the element stuck mid-drag.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/pointercancel_event">MDN</see>
    /// </summary>
    public Callback<PointerEventArgs>? OnPointerCancel { get => Handler<PointerEventArgs>("pointercancel"); set => SetHandler("pointercancel", value?.Handler); }

    // ---- Touch events (TouchEventArgs: active touch count + first-touch coords + modifiers) ----

    /// <summary>
    ///     A finger touched this element. Only reach for the touch events when you need per-finger detail; the
    ///     pointer events cover touch as well and cost one handler instead of two.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/touchstart_event">MDN</see>
    /// </summary>
    public Callback<TouchEventArgs>? OnTouchStart { get => Handler<TouchEventArgs>("touchstart"); set => SetHandler("touchstart", value?.Handler); }

    /// <summary>
    ///     A finger left the screen.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/touchend_event">MDN</see>
    /// </summary>
    public Callback<TouchEventArgs>? OnTouchEnd { get => Handler<TouchEventArgs>("touchend"); set => SetHandler("touchend", value?.Handler); }

    /// <summary>
    ///     A finger moved across this element. Cancelling it stops the page scrolling with the finger, so cancel
    ///     only when the gesture really is yours.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/touchmove_event">MDN</see>
    /// </summary>
    public Callback<TouchEventArgs>? OnTouchMove { get => Handler<TouchEventArgs>("touchmove"); set => SetHandler("touchmove", value?.Handler); }

    /// <summary>
    ///     The browser took over the touch — typically because it became a scroll. Undo whatever the gesture had
    ///     started, the same way a cancelled pointer is handled.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/touchcancel_event">MDN</see>
    /// </summary>
    public Callback<TouchEventArgs>? OnTouchCancel { get => Handler<TouchEventArgs>("touchcancel"); set => SetHandler("touchcancel", value?.Handler); }

    // ---- Focus events (parameterless; focus/blur reach Element via capture-phase delegation) ----

    /// <summary>
    ///     This element received focus. Does not bubble — to catch focus arriving anywhere inside a subtree, use
    ///     <c>focusin</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/focus_event">MDN</see>
    /// </summary>
    public Callback? OnFocus { get => Handler("focus"); set => SetHandler("focus", value?.Handler); }

    /// <summary>
    ///     This element lost focus. The natural moment to validate a field: on blur the user has finished typing,
    ///     whereas validating per keystroke shouts at them mid-word. Does not bubble — see <c>focusout</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/blur_event">MDN</see>
    /// </summary>
    public Callback? OnBlur { get => Handler("blur"); set => SetHandler("blur", value?.Handler); }

    /// <summary>
    ///     Focus arrived at this element or anything inside it. The bubbling form of <c>focus</c>, so one handler
    ///     on a container covers every control in it.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/focusin_event">MDN</see>
    /// </summary>
    public Callback? OnFocusIn { get => Handler("focusin"); set => SetHandler("focusin", value?.Handler); }

    /// <summary>
    ///     Focus left this element or anything inside it. The bubbling form of <c>blur</c>. Careful: it fires while
    ///     moving between two children too, so a 'closed the whole group' check has to test where focus actually
    ///     went.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/focusout_event">MDN</see>
    /// </summary>
    public Callback? OnFocusOut { get => Handler("focusout"); set => SetHandler("focusout", value?.Handler); }

    // ---- Drag events that complete the set (dragstart/over/drop/end already exist on Element) ----

    /// <summary>
    ///     Fires continuously while this element is being dragged. It runs at pointer rate, so keep the handler
    ///     cheap and drive visuals from CSS where you can.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/drag_event">MDN</see>
    /// </summary>
    public Callback? OnDrag { get => Handler("drag"); set => SetHandler("drag", value?.Handler); }

    /// <summary>
    ///     A dragged item entered this element. Cancel the event to advertise this element as a drop target — an
    ///     element that never cancels is not one.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragenter_event">MDN</see>
    /// </summary>
    public Callback? OnDragEnter { get => Handler("dragenter"); set => SetHandler("dragenter", value?.Handler); }

    /// <summary>
    ///     A dragged item left this element. Pairs with <c>dragenter</c> to undo whatever hover styling that turned
    ///     on.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/dragleave_event">MDN</see>
    /// </summary>
    public Callback? OnDragLeave { get => Handler("dragleave"); set => SetHandler("dragleave", value?.Handler); }

    // ---- Clipboard events (ClipboardEventArgs: the plain-text payload read during the event) ----

    /// <summary>
    ///     The user copied from this element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/copy_event">MDN</see>
    /// </summary>
    public Callback<ClipboardEventArgs>? OnCopy { get => Handler<ClipboardEventArgs>("copy"); set => SetHandler("copy", value?.Handler); }

    /// <summary>
    ///     The user cut from this element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/cut_event">MDN</see>
    /// </summary>
    public Callback<ClipboardEventArgs>? OnCut { get => Handler<ClipboardEventArgs>("cut"); set => SetHandler("cut", value?.Handler); }

    /// <summary>
    ///     The user pasted into this element. The place to sanitise or reformat pasted content before it lands.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/paste_event">MDN</see>
    /// </summary>
    public Callback<ClipboardEventArgs>? OnPaste { get => Handler<ClipboardEventArgs>("paste"); set => SetHandler("paste", value?.Handler); }

    // ---- Remaining form-ish events (beforeinput carries the inserted text; select/invalid/reset are bare) ----

    /// <summary>
    ///     Fires before the value changes, carrying the text about to be inserted — so it is where input can be
    ///     inspected, and rejected, while the old value is still in place.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/beforeinput_event">MDN</see>
    /// </summary>
    public Callback<string>? OnBeforeInput { get => Handler<string>("beforeinput"); set => SetHandler("beforeinput", value?.Handler); }

    /// <summary>
    ///     The user selected text inside this control.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/select_event">MDN</see>
    /// </summary>
    public Callback? OnSelect { get => Handler("select"); set => SetHandler("select", value?.Handler); }

    /// <summary>
    ///     Constraint validation failed for this control. Fires per control when a submit is blocked, which is the
    ///     hook for replacing the browser's default bubble with your own message.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLInputElement/invalid_event">MDN</see>
    /// </summary>
    public Callback? OnInvalid { get => Handler("invalid"); set => SetHandler("invalid", value?.Handler); }

    /// <summary>
    ///     The form was reset. Any state you keep outside the model has to be rolled back here too, or the visible
    ///     form and your state disagree.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLFormElement/reset_event">MDN</see>
    /// </summary>
    public Callback? OnReset { get => Handler("reset"); set => SetHandler("reset", value?.Handler); }

    // ---- Scroll (ScrollEvent: scrollTop/clientHeight/scrollHeight; rAF-coalesced client-side) ----

    /// <summary>
    ///     This element was scrolled. Fires at scroll rate and after the fact — read positions here, never write
    ///     layout, or you get a scroll-jank feedback loop.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/scroll_event">MDN</see>
    /// </summary>
    public Callback<ScrollEvent>? OnScroll { get => Handler<ScrollEvent>("scroll"); set => SetHandler("scroll", value?.Handler); }

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
