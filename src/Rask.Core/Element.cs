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
    public string? Style { get; set; }

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
    public IReadOnlyDictionary<string, string?>? Data { get; set; }

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

    // The rest of HTML's global attributes (#693). Before these, everything MDN lists as global beyond
    // id/class/style/title/data-*/role/tabindex/aria-*/draggable was UNREACHABLE — not verbose, impossible,
    // because there was no escape hatch either. The sharpest case was `lang`: it existed on <html> only, so
    // the page language worked and a phrase inside it did not, which is WCAG 3.1.2 (Language of Parts).
    //
    // Storage follows what each one costs. Hidden/Inert take two bits each of the flags byte (present +
    // value) like Draggable, because `hidden` is common and allocating a LiveState for it would be a
    // regression. The others hoist into the lazy LiveState like Role/TabIndex/Aria: opt-in and rare, so an
    // element naming none keeps `_live` null and pays nothing.
    private const byte FlagHiddenPresent = 1 << 3;
    private const byte FlagHiddenValue = 1 << 4;
    private const byte FlagInertPresent = 1 << 5;
    private const byte FlagInertValue = 1 << 6;

    /// <summary>
    ///     The global <c>lang</c> attribute — the language of this element's content, as a BCP 47 tag
    ///     (<c>"en"</c>, <c>"en-GB"</c>, <c>"cy"</c>).
    ///     <para>
    ///         Set it on any run of text in a different language from the page. A screen reader switches
    ///         pronunciation on it, and without it a French quotation inside an English page is read with
    ///         English phonetics — which is
    ///         <see href="https://www.w3.org/WAI/WCAG21/Understanding/language-of-parts">WCAG 3.1.2</see>.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/lang">MDN</see>
    /// </summary>
    public string? Lang
    {
        get => LangInternal;
        set => LangInternal = value;
    }

    /// <summary>
    ///     The global <c>dir</c> attribute — text direction: <c>"ltr"</c>, <c>"rtl"</c>, or <c>"auto"</c>
    ///     to let the browser decide from the first strongly-typed character.
    ///     <para>
    ///         <c>"auto"</c> is the right choice for user-supplied text whose language you do not know at
    ///         render time — a name, a comment, a search query.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/dir">MDN</see>
    /// </summary>
    public string? Dir
    {
        get => DirInternal;
        set => DirInternal = value;
    }

    /// <summary>
    ///     The global <c>hidden</c> attribute — hides the element from every presentation, including
    ///     assistive technology.
    ///     <para>
    ///         Prefer this to inventing a display-none class: a class hides it visually while leaving it in
    ///         the accessibility tree, which is how a screen reader ends up announcing something invisible.
    ///         Note CSS can override it, so a stylesheet setting <c>display</c> on the same element wins.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/hidden">MDN</see>
    /// </summary>
    public bool? Hidden
    {
        get => GetFlag(FlagHiddenPresent) ? GetFlag(FlagHiddenValue) : null;
        set
        {
            SetFlag(FlagHiddenPresent, value.HasValue);
            SetFlag(FlagHiddenValue, value.GetValueOrDefault());
        }
    }

    /// <summary>
    ///     The global <c>inert</c> attribute — makes this element and its whole subtree unfocusable,
    ///     unclickable and invisible to assistive technology.
    ///     <para>
    ///         This is the correct primitive behind a modal: mark everything OUTSIDE the dialog inert and
    ///         focus can no longer escape it. Hand-rolled focus traps are where keyboard users get stuck.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/inert">MDN</see>
    /// </summary>
    public bool? Inert
    {
        get => GetFlag(FlagInertPresent) ? GetFlag(FlagInertValue) : null;
        set
        {
            SetFlag(FlagInertPresent, value.HasValue);
            SetFlag(FlagInertValue, value.GetValueOrDefault());
        }
    }

    /// <summary>
    ///     The global <c>popover</c> attribute — makes this element a popover: <c>"auto"</c> (light-dismiss,
    ///     closes others), <c>"manual"</c>, or <c>"hint"</c>.
    ///     <para>
    ///         The browser handles the top layer, dismissal and focus. Pair it with
    ///         <c>Button.PopoverTarget</c>, which opens it without a line of JavaScript.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/popover">MDN</see>
    /// </summary>
    public string? Popover
    {
        get => PopoverInternal;
        set => PopoverInternal = value;
    }

    /// <summary>
    ///     The global <c>contenteditable</c> attribute — <c>"true"</c>, <c>"false"</c> or
    ///     <c>"plaintext-only"</c>. A string rather than a <c>bool?</c> because the third value is the one
    ///     most editors actually want.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/contenteditable">MDN</see>
    /// </summary>
    public string? ContentEditable
    {
        get => ContentEditableInternal;
        set => ContentEditableInternal = value;
    }

    /// <summary>
    ///     The global <c>spellcheck</c> attribute — whether to spell- and grammar-check editable content.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/spellcheck">MDN</see>
    /// </summary>
    public bool? Spellcheck
    {
        get => SpellcheckInternal;
        set => SpellcheckInternal = value;
    }

    /// <summary>
    ///     The global <c>translate</c> attribute — whether translation tools should translate this
    ///     element's text. Set <c>false</c> on a product name, a code sample or a username.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/translate">MDN</see>
    /// </summary>
    public bool? Translate
    {
        get => TranslateInternal;
        set => TranslateInternal = value;
    }

    /// <summary>
    ///     Arbitrary attributes, verbatim — the escape hatch for anything this surface does not name.
    ///     <para>
    ///         Each entry renders <c>{key}="{value}"</c> with the key written as-is and the value
    ///         HTML-encoded, exactly like <c>Data</c> and <c>Aria</c> but with no prefix. This is how you
    ///         reach microdata (<c>itemscope</c>/<c>itemprop</c>), <c>nonce</c>, <c>part</c>/<c>exportparts</c>,
    ///         <c>accesskey</c>, <c>slot</c>, <c>inputmode</c>, and whatever HTML adds next.
    ///     </para>
    ///     <para>
    ///         Nothing is validated or de-duplicated: naming an attribute a typed property already emits
    ///         (<c>class</c>, say) renders it twice, and the browser takes the first. Prefer the typed
    ///         property whenever one exists — it is the documented, checkable route.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes">MDN</see>
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Attributes
    {
        get => AttributesInternal;
        set => AttributesInternal = value;
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

        // The remaining plain global attributes, slotted with the other plain ones (id/class/style/title)
        // and ahead of the prefixed data-*/aria-* groups, so the documented order stays "globals first,
        // grouped". Each is opt-in, so the common element skips all of them on a null check.
        if (Lang is not null)
        {
            AppendAttr(sb, "lang", Lang);
        }

        if (Dir is not null)
        {
            AppendAttr(sb, "dir", Dir);
        }

        if (Hidden is true)
        {
            AppendAttr(sb, "hidden", null);
        }

        if (Inert is true)
        {
            AppendAttr(sb, "inert", null);
        }

        if (Popover is not null)
        {
            AppendAttr(sb, "popover", Popover);
        }

        if (ContentEditable is not null)
        {
            AppendAttr(sb, "contenteditable", ContentEditable);
        }

        if (Spellcheck is { } spellcheck)
        {
            AppendAttr(sb, "spellcheck", spellcheck ? "true" : "false");
        }

        if (Translate is { } translate)
        {
            AppendAttr(sb, "translate", translate ? "yes" : "no");
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

        // Accessibility group: after data-*, before the Attributes escape hatch and any subclass
        // tag-specific attrs (those run after base.WriteAttributes). Documented order in full:
        // id, class, style, title, the plain globals (lang, dir, hidden, inert, popover,
        // contenteditable, spellcheck, translate), data-*, role, tabindex, aria-*, Attributes,
        // then tag-specific.
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

        // The escape hatch, last in the universal block and so immediately before any subclass
        // tag-specifics. Deliberately after every ordered group: these are arbitrary names, and putting
        // them anywhere earlier would make the documented order depend on what a caller happened to pass.
        // An empty prefix reuses the same allocation-conscious walk Data and Aria use.
        if (Attributes is not null)
        {
            AppendPrefixedAttrs(sb, string.Empty, Attributes, skipKey: null);
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
