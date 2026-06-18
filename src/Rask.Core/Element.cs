using System.Globalization;
using System.Text;
using Rask.Core.Live;

namespace Rask.Core;

// HTML element base. Carries the universal HTML attributes (Id/Class/Style/Data) so that
// tag classes (Div, Span, Input, …) inherit them and their generated factories expose them
// as optional parameters. User components extend Component directly and stay free of these
// HTML-only concerns.
public abstract class Element : Component
{
    public string? Id { get; set; }
    public string? Class { get; set; }
    public string? Style { get; set; }
    public IReadOnlyDictionary<string, string?>? Data { get; set; }

    // Accessibility, available on every element. `Aria` is the data-* model applied to ARIA: each
    // entry emits aria-{key}="{value}" (key verbatim, value HTML-encoded) — so `Aria: new() {
    // ["label"] = "Close" }` renders aria-label="Close", and the full ARIA vocabulary is reachable
    // without a typed property per attribute. `Role` and `TabIndex` are plain attributes (not aria-*,
    // so not expressible through the dictionary) but are core a11y affordances for custom widgets and
    // keyboard focus. All three are nullable → optional factory parameters, like the other HTML attrs.
    // Like Ref, their storage is hoisted into the lazy LiveState (a11y attrs are opt-in and rare), so
    // an element that sets none keeps `_live` null and pays no per-instance footprint for the feature.
    public string? Role
    {
        get => RoleInternal;
        set => RoleInternal = value;
    }

    public int? TabIndex
    {
        get => TabIndexInternal;
        set => TabIndexInternal = value;
    }

    public IReadOnlyDictionary<string, string?>? Aria
    {
        get => AriaInternal;
        set => AriaInternal = value;
    }

    // A stable DOM handle for JS interop. When set, emits data-rask-ref="{id}" in the data-* group;
    // the client reviver resolves an ElementRef arg to this element via [data-rask-ref="..."].
    // Storage is hoisted into the lazy LiveState (ElementRefInternal) so a ref-less element keeps
    // `_live` null and adds zero footprint — direct fields on Element are what the LiveState hoist
    // exists to avoid. The generator special-cases ElementRef to an optional factory parameter
    // (Blazor @ref parity, available on every element).
    public ElementRef? Ref
    {
        get => ElementRefInternal;
        set => ElementRefInternal = value;
    }

    // Native HTML5 drag-and-drop, available on every element. `Draggable` emits draggable="true"
    // (nullable so it stays an optional factory param — Blazor-parity with the other HTML attrs);
    // the four handlers bind the dragstart/dragover/drop/dragend DOM events to parameterless C#
    // delegates, wired by the client runtime via data-rask-on-drag*. Each event ships a sync
    // `OnXxx` (Action) and an async `OnXxxAsync` (Func<Task>) sibling — the typed-pair convention
    // shared with OnClick/OnClickAsync; set at most one per event (sync wins if both are set). The
    // dragged item's identity is carried by the handler's closure, not the event payload — see the
    // headless DragDrop primitive (DragDrop.cs) and the DragDropContext it hands consumers. Storage
    // is hoisted into the lazy LiveState (like Ref/Role/Aria) so an element that wires no drag
    // handler keeps `_live` null and pays no per-instance footprint.
    public bool? Draggable { get; set; }

    // Each drag event has a single backing slot (kept as a direct Element field — drag is rare but
    // pre-existing, and a direct field avoids growing the per-render LiveState that the
    // allocation-pin tests guard). The sync `OnXxx` (Action) and async `OnXxxAsync` (Func<Task>)
    // properties are two typed views over that slot, distinguished by delegate type: a setter only
    // clears the slot when it currently holds *its own* kind, so the generated factory re-applying
    // both (one null) every render can't clobber the handler the caller actually set. Set at most
    // one per event.
    private Delegate? _onDragStart;
    private Delegate? _onDragOver;
    private Delegate? _onDrop;
    private Delegate? _onDragEnd;

    public Action? OnDragStart
    {
        get => _onDragStart as Action;
        set => SetSync(ref _onDragStart, value);
    }

    public Func<Task>? OnDragStartAsync
    {
        get => _onDragStart as Func<Task>;
        set => SetAsync(ref _onDragStart, value);
    }

    public Action? OnDragOver
    {
        get => _onDragOver as Action;
        set => SetSync(ref _onDragOver, value);
    }

    public Func<Task>? OnDragOverAsync
    {
        get => _onDragOver as Func<Task>;
        set => SetAsync(ref _onDragOver, value);
    }

    public Action? OnDrop
    {
        get => _onDrop as Action;
        set => SetSync(ref _onDrop, value);
    }

    public Func<Task>? OnDropAsync
    {
        get => _onDrop as Func<Task>;
        set => SetAsync(ref _onDrop, value);
    }

    public Action? OnDragEnd
    {
        get => _onDragEnd as Action;
        set => SetSync(ref _onDragEnd, value);
    }

    public Func<Task>? OnDragEndAsync
    {
        get => _onDragEnd as Func<Task>;
        set => SetAsync(ref _onDragEnd, value);
    }

    // Keyboard events, available on every element (a key event needs the element — or a descendant
    // — focused, the same focus-scoped model as click). Bound by the client runtime via
    // data-rask-on-keydown / data-rask-on-keyup. Each ships a sync `OnXxx`
    // (Action<KeyboardEventArgs>) and an async `OnXxxAsync` (Func<KeyboardEventArgs, Task>) sibling
    // coalesced over a single LiveState slot by delegate type (like the drag pairs above); the
    // dispatcher unpacks the {key, code, modifiers, repeat} payload into a KeyboardEventArgs. The
    // client never preventDefaults a key event, so handlers compose with normal typing. Storage is
    // hoisted into the lazy LiveState (like Ref/Role/Aria) so an element with no key handler keeps
    // `_live` null and pays no per-instance footprint — see OnKeyDownInternal/OnKeyUpInternal.
    public Action<KeyboardEventArgs>? OnKeyDown
    {
        get => OnKeyDownInternal as Action<KeyboardEventArgs>;
        set
        {
            if (value is not null)
            {
                OnKeyDownInternal = value;
            }
            else if (OnKeyDownInternal is Action<KeyboardEventArgs>)
            {
                OnKeyDownInternal = null;
            }
        }
    }

    public Func<KeyboardEventArgs, Task>? OnKeyDownAsync
    {
        get => OnKeyDownInternal as Func<KeyboardEventArgs, Task>;
        set
        {
            if (value is not null)
            {
                OnKeyDownInternal = value;
            }
            else if (OnKeyDownInternal is Func<KeyboardEventArgs, Task>)
            {
                OnKeyDownInternal = null;
            }
        }
    }

    public Action<KeyboardEventArgs>? OnKeyUp
    {
        get => OnKeyUpInternal as Action<KeyboardEventArgs>;
        set
        {
            if (value is not null)
            {
                OnKeyUpInternal = value;
            }
            else if (OnKeyUpInternal is Action<KeyboardEventArgs>)
            {
                OnKeyUpInternal = null;
            }
        }
    }

    public Func<KeyboardEventArgs, Task>? OnKeyUpAsync
    {
        get => OnKeyUpInternal as Func<KeyboardEventArgs, Task>;
        set
        {
            if (value is not null)
            {
                OnKeyUpInternal = value;
            }
            else if (OnKeyUpInternal is Func<KeyboardEventArgs, Task>)
            {
                OnKeyUpInternal = null;
            }
        }
    }

    // Shared coalescing helpers for the drag handler pairs: a non-null value always wins; a null
    // only clears the slot when it currently holds the same kind (sync Action vs async Func<Task>),
    // so re-applying the unset sibling never wipes the handler the caller set.
    private static void SetSync(ref Delegate? slot, Action? value)
    {
        if (value is not null)
        {
            slot = value;
        }
        else if (slot is Action)
        {
            slot = null;
        }
    }

    private static void SetAsync(ref Delegate? slot, Func<Task>? value)
    {
        if (value is not null)
        {
            slot = value;
        }
        else if (slot is Func<Task>)
        {
            slot = null;
        }
    }

    // Subclasses transform the `class` attribute value without re-implementing the universal
    // id/class/style/data-* walk. NavLink overrides this to splice in its active class.
    protected virtual string? ResolveClass() => Class;

    protected override void WriteAttributes(StringBuilder sb)
    {
        if (Id is not null)
        {
            AppendAttr(sb, "id", Id);
        }

        var cls = ResolveClass();
        if (cls is not null)
        {
            AppendAttr(sb, "class", cls);
        }

        if (Style is not null)
        {
            AppendAttr(sb, "style", Style);
        }

        // Effective keyed-list identity: this element's own Key, else a key forwarded from a
        // transparent ancestor component (Consume clears the slot so only the FIRST element
        // adopts it). Emitted in the data-* group below so FrameDiffer.ExtractRaskKey finds it
        // among the leading attribute frames, same as a Data["rask-key"] entry.
        var forwarded = KeyForwardScope.Consume();
        var key = KeyString ?? forwarded;

        if (Data is not null)
        {
            foreach (var kv in Data)
            {
                // A literal Data["rask-key"] is superseded by an effective Key to avoid a
                // duplicate attribute — Key is the canonical API; Data stays for back-compat.
                if (key is not null && string.Equals(kv.Key, "rask-key", StringComparison.Ordinal))
                {
                    continue;
                }

                AppendAttr(sb, "data-", kv.Key, kv.Value);
            }
        }

        if (key is not null)
        {
            AppendAttr(sb, "data-", "rask-key", key);
        }

        // Element ref handle (JS interop): a data-* attribute, emitted alongside rask-key so it
        // sits in the universal data-* group, before drag hooks and tag-specifics.
        if (Ref is { } elementRef)
        {
            AppendAttr(sb, "data-", "rask-ref", elementRef.Id);
        }

        // Drag-and-drop: a universal attribute (draggable) plus the data-rask-on-drag* handler
        // hooks. Emitted here in the universal section, before subclass tag-specifics (which run
        // after base.WriteAttributes). Unset (null / no handler) emits nothing.
        if (Draggable is true)
        {
            AppendAttr(sb, "draggable", "true");
        }

        if (LiveRenderContext.CurrentSync is { } ctx)
        {
            // Each event reads its single backing slot (holding the sync or async delegate the
            // caller set); the dispatcher routes Action vs Func<Task> by the delegate's runtime type.
            if (_onDragStart is not null)
            {
                AppendAttr(sb, "data-rask-on-dragstart", ctx.RegisterHandler(_onDragStart));
            }

            if (_onDragOver is not null)
            {
                AppendAttr(sb, "data-rask-on-dragover", ctx.RegisterHandler(_onDragOver));
            }

            if (_onDrop is not null)
            {
                AppendAttr(sb, "data-rask-on-drop", ctx.RegisterHandler(_onDrop));
            }

            if (_onDragEnd is not null)
            {
                AppendAttr(sb, "data-rask-on-dragend", ctx.RegisterHandler(_onDragEnd));
            }

            if (OnKeyDownInternal is { } keyDown)
            {
                AppendAttr(sb, "data-rask-on-keydown", ctx.RegisterHandler(keyDown));
            }

            if (OnKeyUpInternal is { } keyUp)
            {
                AppendAttr(sb, "data-rask-on-keyup", ctx.RegisterHandler(keyUp));
            }
        }

        // Accessibility group, last in the universal block so it lands after data-* yet before any
        // subclass tag-specific attrs (those run after base.WriteAttributes). Documented order:
        // id, class, style, data-*, role, tabindex, aria-*, then tag-specific.
        if (Role is not null)
        {
            AppendAttr(sb, "role", Role);
        }

        if (TabIndex is not null)
        {
            AppendAttr(sb, "tabindex", TabIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Aria is not null)
        {
            foreach (var kv in Aria)
            {
                AppendAttr(sb, "aria-", kv.Key, kv.Value);
            }
        }
    }
}
