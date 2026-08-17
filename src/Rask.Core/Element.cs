using System.Text;
using Rask.Core.Live;

namespace Rask.Core;

// HTML element base. Carries the universal HTML attributes (Id/Class/Style/Data) so that
// tag classes (Div, Span, Input, …) inherit them and their generated factories expose them
// as optional parameters. User components extend Component directly and stay free of these
// HTML-only concerns.
public abstract partial class Element : Component
{
    /// <summary>
    ///     The global <c>id</c> attribute — this element's unique identifier in the document. It is what a
    ///     <c>#fragment</c> link scrolls to, what a <c>label</c>'s <c>for</c> points at, and what
    ///     <c>aria-labelledby</c> / <c>aria-describedby</c> reference.
    ///     <para>
    ///         It must be unique across the whole page, so treat it as a scarce resource: reach for a class
    ///         or a <see cref="Data" /> attribute when you only need to find or style an element, and spend
    ///         an id where something else has to point at this one by name.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/id">MDN</see>
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    ///     The global <c>class</c> attribute — a space-separated list of class names, the usual hook for CSS
    ///     and for finding an element from script.
    ///     <para>
    ///         The whole string is the value, so composing one conditionally means composing the string:
    ///         <c>.Class(active ? "tab active" : "tab")</c>. Scoped CSS is applied separately and does not
    ///         go through here.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/class">MDN</see>
    /// </summary>
    public string? Class { get; set; }

    /// <summary>
    ///     The global <c>style</c> attribute — CSS declarations applied to this element alone.
    ///     <para>
    ///         Inline style beats every stylesheet rule short of <c>!important</c> and cannot be overridden
    ///         from a theme, so keep it for values only known at runtime (a computed width, a progress
    ///         offset) and put the rest in scoped CSS, where it stays themeable and cacheable.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/style">MDN</see>
    /// </summary>
    public new string? Style { get; set; }

    /// <summary>
    ///     Custom <c>data-*</c> attributes. Each entry emits <c>data-{key}="{value}"</c> — the key verbatim,
    ///     the value HTML-encoded — so <c>.Data("test-id", "submit")</c> renders
    ///     <c>data-test-id="submit"</c>. A <see langword="null" /> value renders the attribute bare, the way
    ///     <c>disabled</c> is written; <c>""</c> renders <c>=""</c>, which is a different attribute.
    ///     <para>
    ///         This is the supported place to hang your own metadata on an element — test hooks, values a
    ///         piece of interop JS reads back through <c>dataset</c>. Names must be lowercase and
    ///         hyphenated, never camelCase.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/data-*">MDN</see>
    /// </summary>
    public new IReadOnlyDictionary<string, string?>? Data { get; set; }

    // Accessibility, available on every element. `Aria` is the data-* model applied to ARIA: each
    // entry emits aria-{key}="{value}" (key verbatim, value HTML-encoded) — so `Aria: new() {
    // ["label"] = "Close" }` renders aria-label="Close", and the full ARIA vocabulary is reachable
    // without a typed property per attribute. `Role` and `TabIndex` are plain attributes (not aria-*,
    // so not expressible through the dictionary) but are core a11y affordances for custom widgets and
    // keyboard focus. All three are nullable → optional factory parameters, like the other HTML attrs.
    // Like Ref, their storage is hoisted into the lazy LiveState (a11y attrs are opt-in and rare), so
    // an element that sets none keeps `_live` null and pays no per-instance footprint for the feature.
    /// <summary>
    ///     The ARIA <c>role</c> — what this element *is* to assistive technology, when the tag alone does
    ///     not say it. A <c>div</c> wired up as a tab strip needs <c>.Role("tablist")</c>; a
    ///     <see cref="Components.Button" /> already reports itself as a button and needs nothing.
    ///     <para>
    ///         Prefer the native element over a role every time one exists. A role changes only what is
    ///         announced — it does not bring the keyboard behaviour, focus handling or state the real
    ///         element has, so overriding semantics you have not also implemented makes a control less
    ///         usable, not more.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Reference/Roles">MDN</see>
    /// </summary>
    public string? Role
    {
        get => RoleInternal;
        set => RoleInternal = value;
    }

    /// <summary>
    ///     The global <c>tabindex</c> attribute — whether, and where, this element sits in the keyboard tab
    ///     order. <c>0</c> makes it focusable in document order; <c>-1</c> makes it focusable only from
    ///     script (<c>.Focus()</c>), which is what a roving-focus widget or a scroll target wants.
    ///     <para>
    ///         A positive value jumps the element ahead of everything with <c>0</c>, across the entire page,
    ///         and is almost always a bug: it makes tab order depend on numbers scattered through unrelated
    ///         components. Order the markup instead.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/tabindex">MDN</see>
    /// </summary>
    public int? TabIndex
    {
        get => TabIndexInternal;
        set => TabIndexInternal = value;
    }

    /// <summary>
    ///     ARIA states and properties. Each entry emits <c>aria-{key}="{value}"</c> — the key verbatim, the
    ///     value HTML-encoded — so <c>.Aria("label", "Close")</c> renders <c>aria-label="Close"</c>. The
    ///     whole ARIA vocabulary is reachable this way, with no typed property per attribute.
    ///     <para>
    ///         State belongs here, not just labels: <c>aria-expanded</c>, <c>aria-selected</c> and
    ///         <c>aria-checked</c> have to be re-rendered as the value changes, or a screen-reader user is
    ///         told the opposite of what is on screen. Only reach for <c>aria-label</c> when there is no
    ///         visible text to point at with <c>aria-labelledby</c> — a visible label serves everyone.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Reference/Attributes">MDN</see>
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Aria
    {
        get => AriaInternal;
        set => AriaInternal = value;
    }

    /// <summary>
    ///     The global <c>title</c> attribute — advisory text the browser shows as a tooltip on hover.
    ///     Useful wherever a cell shows an abbreviated value and the precise one belongs behind it: a
    ///     relative timestamp over the exact instant, a truncated string over its full text.
    ///     <para>
    ///         Not a substitute for a label. <c>title</c> is invisible to touch users, unreliable for
    ///         screen readers, and cannot be focused — so it may carry supplementary detail, never the
    ///         only copy of something the user needs. For an accessible name use <see cref="Aria" />.
    ///     </para>
    ///     <para>
    ///         Declared last among Element's own properties on purpose. Factory parameters are ordered
    ///         derived-first, then by declaration span, so inserting this next to <see cref="Style" />
    ///         would have shifted the positional index of Data/Role/TabIndex/Aria for every element in
    ///         the framework — a silent source break for anyone passing them positionally.
    ///     </para>
    /// </summary>
    public new string? Title { get; set; }

    // A stable DOM handle for JS interop. When set, emits data-rask-ref="{id}" in the data-* group;
    // the client reviver resolves an ElementRef arg to this element via [data-rask-ref="..."].
    // Storage is hoisted into the lazy LiveState (ElementRefInternal) so a ref-less element keeps
    // `_live` null and adds zero footprint — direct fields on Element are what the LiveState hoist
    // exists to avoid. The generator special-cases ElementRef to an optional factory parameter
    // (Blazor @ref parity, available on every element).
    /// <summary>
    ///     A stable handle on the rendered DOM node, for the cases that genuinely need one — focusing an
    ///     input, measuring a box, handing the element to a JS library. Create it once in a field with
    ///     <c>ElementRef.New()</c>, set it here, and pass it to <c>IJSRuntime</c>; it survives re-renders,
    ///     and the client resolves it through the <c>data-rask-ref</c> attribute this emits.
    ///     <para>
    ///         It is a way *out* of the render model, so it is not the tool for changing what is on screen:
    ///         anything you write to the DOM by hand is invisible to the diff and is overwritten by the next
    ///         render. Drive appearance from state and keep the ref for what only the real node can answer.
    ///     </para>
    /// </summary>
    public ElementRef? Ref
    {
        get => ElementRefInternal;
        set => ElementRefInternal = value;
    }

    // Native HTML5 drag-and-drop attribute, available on every element. `Draggable` emits
    // draggable="true" (nullable so it stays an optional factory param — Blazor-parity with the other
    // HTML attrs). The drag *handlers* (OnDragStart/Over/Drop/End plus drag/dragenter/dragleave) live on
    // the unified GlobalEventHandlers surface in ElementEvents.cs, like every other event.
    // Backed by two bits of the base Component flags byte (present + value) instead of a dedicated
    // Nullable<bool> field, so a drag-less element carries no extra slot — see Component._flags.
    private const byte FlagDraggablePresent = 1 << 1;
    private const byte FlagDraggableValue = 1 << 2;

    /// <summary>
    ///     The global <c>draggable</c> attribute — marks this element as a drag source for native HTML
    ///     drag-and-drop. Set it together with an <c>OnDragStart</c> handler that puts something on the
    ///     data transfer, or the drag starts and carries nothing.
    ///     <para>
    ///         Native drag-and-drop is a pointer gesture with no keyboard or touch equivalent, so whatever
    ///         it does must also be reachable another way — a menu item, a pair of move buttons. It is an
    ///         accelerator, never the only route.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/draggable">MDN</see>
    /// </summary>
    public bool? Draggable
    {
        get => GetFlag(FlagDraggablePresent) ? GetFlag(FlagDraggableValue) : null;
        set
        {
            SetFlag(FlagDraggablePresent, value.HasValue);
            SetFlag(FlagDraggableValue, value.GetValueOrDefault());
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

        // Slotted with the other plain global attributes (id/class/style) and ahead of the prefixed
        // data-*/aria-* groups, so the documented order stays "globals first, grouped".
        if (Title is not null)
        {
            AppendAttr(sb, "title", Title);
        }

        // Effective keyed-list identity: this element's own Key, else a key forwarded from a
        // transparent ancestor component (Consume clears the slot so only the FIRST element
        // adopts it). Emitted in the data-* group below so FrameDiffer.ExtractRaskKey finds it
        // among the leading attribute frames, same as a Data["rask-key"] entry.
        var forwarded = KeyForwardScope.Consume();
        var key = KeyString ?? forwarded;

        if (Data is not null)
        {
            // A literal Data["rask-key"] is superseded by an effective Key to avoid a duplicate
            // attribute — Key is the canonical API; Data stays for back-compat.
            AppendPrefixedAttrs(sb, "data-", Data, key is not null ? "rask-key" : null);
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

        // The full GlobalEventHandlers surface — drag, keyboard, click, scroll, mouse, pointer, touch,
        // focus, clipboard, wheel — is emitted by EmitDomEvents in one fixed order from the unified
        // DomEvents store (see ElementEvents.cs). data-rask-on-* hooks register a handler id per wired
        // event; a plain element with no handlers early-outs in one null check.
        if (LiveRenderContext.CurrentSync is { } ctx)
        {
            EmitDomEvents(sb, ctx);
        }

        // Accessibility group, last in the universal block so it lands after data-* yet before any
        // subclass tag-specific attrs (those run after base.WriteAttributes). Documented order:
        // id, class, style, data-*, role, tabindex, aria-*, then tag-specific.
        if (Role is not null)
        {
            AppendAttr(sb, "role", Role);
        }

        if (TabIndex is { } tabIndex)
        {
            AppendAttr(sb, "tabindex", tabIndex);
        }

        if (Aria is not null)
        {
            AppendPrefixedAttrs(sb, "aria-", Aria, skipKey: null);
        }
    }

    // Emit each entry of a data-*/aria-* bag as "{prefix}{key}=\"{value}\"". Iterating a concrete
    // Dictionary<,> uses its struct enumerator (no allocation); foreach over the
    // IReadOnlyDictionary interface instead boxes an enumerator on every render of an element that
    // carries a Data or Aria bag — the common literal (`new() { ... }`) is a Dictionary, so it
    // takes the fast path. `skipKey`, when set, drops one entry (Data["rask-key"] superseded by Key).
    private static void AppendPrefixedAttrs(StringBuilder sb, string prefix,
        IReadOnlyDictionary<string, string?> map, string? skipKey)
    {
        // The bag `.Data("test-id", "primary")` builds. Written straight from its fields — it has no
        // struct enumerator to borrow, so without this branch the single-attribute case would trade
        // Dictionary's three allocations for a boxed enumerator on every render.
        if (map is AttrBag bag)
        {
            if (skipKey is null || !string.Equals(bag.Name0, skipKey, StringComparison.Ordinal))
            {
                AppendAttr(sb, prefix, bag.Name0, bag.Value0);
            }

            if (bag.Rest is { } rest)
            {
                foreach (var kv in rest)
                {
                    if (skipKey is null || !string.Equals(kv.Key, skipKey, StringComparison.Ordinal))
                    {
                        AppendAttr(sb, prefix, kv.Key, kv.Value);
                    }
                }
            }

            return;
        }

        if (map is Dictionary<string, string?> dict)
        {
            foreach (var kv in dict)
            {
                if (skipKey is null || !string.Equals(kv.Key, skipKey, StringComparison.Ordinal))
                {
                    AppendAttr(sb, prefix, kv.Key, kv.Value);
                }
            }
        }
        else
        {
            foreach (var kv in map)
            {
                if (skipKey is null || !string.Equals(kv.Key, skipKey, StringComparison.Ordinal))
                {
                    AppendAttr(sb, prefix, kv.Key, kv.Value);
                }
            }
        }
    }
}
