using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Rask.Core.Components;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Core.HeadAssets;
using Rask.Core.Live;

namespace Rask.Core;

// [CollectionBuilder] makes `Component` itself a collection-expression target, so a render body
// can be written as `Render() => [Nav(), Main()[Router()]]` (the items are built into a Fragment by
// __Fragment below). The builder is self-referential (typeof(Component)) and public so collection
// expressions in *other* assemblies bind to it even though Fragment itself is internal. The
// required iteration type comes from the *pattern* GetEnumerator below — Component deliberately
// does NOT implement IEnumerable<Component>, because that would make the `this[IEnumerable<Component>]`
// children indexer applicable to a bare component and silently rebind `Div()[Span()[...]]` from
// "one child" to "the span's own children", collapsing nesting.
[CollectionBuilder(typeof(Component), "__Fragment")]
public abstract partial class Component : RaskMarkup
{
    // Pre-built "h0".."h1023" so minting a handler slot's id in the common case (small forms, typical
    // pages) doesn't pay a string allocation. A slot past the table falls back to CreateLargeHandlerId
    // — once, because the id string is then cached on the slot for the component's lifetime.
    private static readonly string[] _smallHandlerIds = BuildSmallHandlerIds(1024);

    // Process-wide source for HandlerState.Generation. Renders are serialized per session but NOT
    // across sessions, so a per-root counter would hand two concurrently-rendering roots the same
    // generation numbers. A shared monotonic source makes a generation globally unique, so a component
    // can never read a stale stamp as "already reset this frame". long, so it cannot wrap in any
    // realistic uptime.
    private static long _renderGenerationSource;

    // Static empty dict for PersistedChildren exposed via the public-internal accessor —
    // saves callers from null checks while keeping the per-instance allocation lazy.
    private static readonly Dictionary<(Type, int), Component> _emptyChildren = new();

    // Per-node boolean state packed into one byte so it costs a single field slot instead of one
    // (padded) slot per bool across the Component/Element pair. Bit 0 lives on the base; Element
    // claims bits 1-2 (see Element.Draggable). GetFlag/SetFlag are private protected so a derived
    // Element in this assembly can share the byte. Reserve new bits here to keep the allocation
    // documented in one place.
    //   bit 0 — reads-ambient-state (below)
    //   bit 1 — Element: Draggable present
    //   bit 2 — Element: Draggable value
    //   bit 3 — a chain assigned a callback prop (below)
    private byte _flags;

    // Set the first time this component reads untracked ambient state during Render: a context value
    // (Context.Get/Required/Has-via-Get) OR EditContext state (validation messages / validating flags,
    // via EditContext.MarkReader). Such a component depends on state the framework doesn't diff, so —
    // like BypassRenderCache — it must re-execute Render() on every walk to pick up a changed value.
    // This is why form controls that read validation state need no manual BypassRenderCache override.
    // Latched on: once a reader, always a reader (its Render path can read different
    // context/edit-context state across renders).
    private const byte FlagReadsAmbientState = 1 << 0;

    private bool _readsAmbientState => (_flags & FlagReadsAmbientState) != 0;

    // Set by a chain setter assigning a CALLBACK prop; read-and-cleared by the eager reset that acts
    // on it.
    //
    // The eager reset runs at the entry, BEFORE this render's setters, and puts every non-folding prop
    // back to its default so a callback the chain named last render cannot survive into one where it
    // does not. On Element that is ~88 delegate fields written unconditionally, on every entry-built
    // element, on every render — and the overwhelming majority of elements carry no callback at all,
    // so nearly all of those writes were assigning null over null. This bit means "there is something
    // to clear", which lets the common element skip the block entirely.
    //
    // Only callbacks set it. `Key` is non-folding too, but it is a single write, and gating it here
    // would mean a Key-only chain still paid for the whole delegate block.
    private const byte FlagCallbackAssigned = 1 << 3;

    internal void MarkCallbackAssignedInternal() => SetFlag(FlagCallbackAssigned, true);

    // Peek, not take. One reset pass can run several of these routines — a component's own eager reset
    // calls the shared Element one first — and they must all reach the same answer, so the bit is
    // cleared once by the entry that ran them (BuilderRuntime.Entry) rather than by whichever routine
    // happened to look first.
    internal bool HasCallbackAssignedInternal() => (_flags & FlagCallbackAssigned) != 0;

    internal void ClearCallbackAssignedInternal() => SetFlag(FlagCallbackAssigned, false);

    private protected bool GetFlag(byte mask) => (_flags & mask) != 0;

    private protected void SetFlag(byte mask, bool value) =>
        _flags = value ? (byte)(_flags | mask) : (byte)(_flags & ~mask);

    // All live-render-only state — handlers, child reconciliation, root alive sets, the error
    // boundary + render handle + lifetime token, edit-context pool, the dirty/lifecycle flags — is
    // hoisted off the base Component class into a lazy container. Plain Elements (Div, Span, …) never
    // engage any of these paths and so keep `_live` null forever: their per-instance footprint is just
    // the object header + a Children ref. User components and live-render roots pay one LiveState
    // allocation on first use; subsequent renders reuse it via the pooled dictionaries inside.
    private LiveState? _live;

    private LiveState Live => _live ??= new LiveState();

    // Set by the children indexer below. Factories no longer expose Children as a parameter —
    // `Div()[Span(...), "hi"]` is the canonical call shape. Elements are nullable: a `null` child
    // renders nothing, so an inline `cond ? node : null` needs no placeholder.
    public IEnumerable<Component?>? Children { get; set; }

    // Present ONLY to give the [CollectionBuilder] attribute above an iteration type of
    // `Component?` (see CS9188). This is the *enumerable pattern* — a public GetEnumerator — and is
    // intentionally NOT `IEnumerable<Component?>`: implementing the interface would rebind the
    // `this[IEnumerable<Component?>]` indexer and collapse nested components (see class remark).
    // Enumerating a component walks its children.
    public IEnumerator<Component?> GetEnumerator() => (Children ?? []).GetEnumerator();

    // Collection-expression builder targeted by the [CollectionBuilder] attribute above. A
    // `Render()`/`Head` body written as `[Doctype(), Html(...)]` lands here and is wrapped in a
    // (tagless, internal) Fragment so the whole render pipeline keeps operating on a single
    // Component. Public because the compiler emits this call at each collection-expression site,
    // including in user assemblies where Fragment is not visible. NOT named `Create`: that would
    // shadow a user component named `Create` (its generated factory) via base-member lookup.
    public static Component __Fragment(ReadOnlySpan<Component?> items) => new Fragment(items.ToArray());

    // Heterogeneous-literal children: `Div()["Score: ", 42, Span()]`. These implicit conversions
    // (formerly on the deleted `Component` struct) let strings/primitives/dates flow into a children
    // list as auto-created Text nodes. Value types render with InvariantCulture so the HTML stays
    // locale-independent and byte-stable for the diff codec — matching Forms/BindingHelpers and
    // RouteValueParser. Narrower integer types widen to `int`; `char` renders the character.
    // Accepts string? so a nullable expression (e.g. `entity.Value?.ToString()`) can flow straight into
    // a children list; null becomes an empty text node rather than forcing callers to write `?? ""`.
    public static implicit operator Component(string? text) => new Text { Value = text ?? "" };
    public static implicit operator Component(int value) => Format(value);
    public static implicit operator Component(long value) => Format(value);
    public static implicit operator Component(double value) => Format(value);
    public static implicit operator Component(float value) => Format(value);
    public static implicit operator Component(decimal value) => Format(value);
    public static implicit operator Component(bool value) => new Text { Value = value ? "True" : "False" };
    public static implicit operator Component(char value) => new Text { Value = value.ToString() };
    public static implicit operator Component(Guid value) => new Text { Value = value.ToString() };
    public static implicit operator Component(DateOnly value) => Format(value);
    public static implicit operator Component(TimeOnly value) => Format(value);
    public static implicit operator Component(DateTime value) => Format(value);
    public static implicit operator Component(DateTimeOffset value) => Format(value);
    public static implicit operator Component(TimeSpan value) => Format(value);

    private static Component Format<T>(T value)
        where T : IFormattable =>
        new Text { Value = value.ToString(null, CultureInfo.InvariantCulture) };

    // Stable identity for keyed list reconciliation (Blazor `@key` parity). When set on an
    // element it emits `data-rask-key`; when set on a transparent component (a custom
    // component / Fragment) the serializer forwards it onto that component's FIRST rendered
    // element (see HtmlSerializer + KeyForwardScope). The diff codec reads the attribute
    // (FrameDiffer.ExtractRaskKey) to match siblings by identity and ship TRUSTED structural
    // ops (Insert/Remove/Move) instead of a positional full-HTML morph. `object?` so callers
    // pass a Guid/int/string directly; stringified on emit. Nullable + no initializer ⇒ the
    // factory generator exposes it as an optional `Key:` parameter on every factory.
    // Keyed insert/append/delete/move/in-place edits are all correct, including when the
    // structural change rides a navigation diff (the inserted row's HTML fragment is sliced from
    // post-head-splice HTML via offsets that RenderAsLiveRootCore keeps in lockstep — see
    // FrameWriter.AdjustOffsetsFrom and KeyedInsertNavTests).
    /// <summary>
    ///     A stable identity for this item in a list, so the diff can tell "the same row moved" from "a
    ///     different row is now in this position". Give it something that belongs to the data —
    ///     <c>.Key(todo.Id)</c> — never the loop index, which changes the moment anything is inserted or
    ///     removed and so identifies nothing.
    ///     <para>
    ///         Without it, a list is matched by position: inserting at the top rewrites every row below,
    ///         and any state living in the real DOM rather than in your model — focus, text selection,
    ///         scroll position, a half-typed input — moves to the wrong row. With it, the same edit ships
    ///         as one insert.
    ///     </para>
    ///     <para>
    ///         It is an identity, not a prop: changing it does not re-render anything, it tells the
    ///         reconciler this is a <em>different</em> item. Keys only need to be unique among siblings.
    ///     </para>
    /// </summary>
    public object? Key { get; set; }

    // Stringified Key for emit (data-rask-key) and key-forwarding, computed per render on every
    // keyed node. A string key needs no allocation (ToString() returns itself); a value key
    // (int/Guid) allocates a small string per render.
    //
    // We deliberately do NOT cache the value→string mapping: the cache only ever hit for a keyed
    // instance that is REUSED across renders, but a keyed list rebuilds its element instances every
    // render, so the cache was cold-missing there anyway. Keeping it cost two reference fields
    // (16 B) on EVERY node in a mounted tree — a bad trade against a rare ToString on reused nodes,
    // and this is a footprint-focused path. Non-keyed nodes (the majority) hit the null short-circuit
    // and allocate nothing.
    //
    // Every non-string arm formats with InvariantCulture. The key is stringified into data-rask-key and
    // baked into the HTML the client already holds, so a culture-sensitive spelling breaks keyed
    // reconciliation at the moment the culture changes: under sv-SE a negative int renders with U+2212
    // MINUS SIGN rather than '-', and a decimal/DateTime key re-spells entirely. int/long/Guid lead
    // because they are the documented common keys and stay a direct call; IFormattable catches
    // decimal/double/DateTime/DateOnly/TimeOnly and formattable enums.
    internal string? KeyString => Key switch
    {
        null => null,
        string s => s,
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        Guid g => g.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var k => k.ToString(),
    };

    // Primary children indexer. `Div()[Span(...), "hi"]` is the call shape: literal lists of
    // components/strings (each implicitly a Component via the converters above). Overload
    // resolution prefers this `params Component[]` form over the IEnumerable<…> variant below —
    // the compiler emits a single `new Component[N]{ … }` and we assign it directly, no copy.
    public Component this[params Component?[] children]
    {
        get
        {
            Children = children;
            return this;
        }
    }

    // Single-arg enumerable form: a `List<Component>`, a `.Select(...)` LINQ projection, or any
    // pre-built `IEnumerable<Component>`. The compiler picks this over the `params Component[]`
    // overload only when the arg is a single sequence that isn't already a `Component[]`.
    public Component this[IEnumerable<Component?> children]
    {
        get
        {
            // Materialise a *lazy* sequence (a `yield`/LINQ pipeline that hasn't been evaluated)
            // right here, during Render. A component may be built by a factory, and those factories
            // must run NOW — inside the owning component's render walk, where child-reuse bookkeeping
            // (GetOrCreateChild's position map + PreviousChildren swap) is live — not later during
            // serialization when that state is gone. Deferring would recreate any embedded component
            // every render and silently drop its state (e.g. a demo mounted from a yield-built list).
            // Already-materialised collections (Component?[]/List<Component?>/…) ran their factories
            // when the caller built them, so they pass through without a copy.
            Children = children is IReadOnlyCollection<Component?> ? children : children.ToArray();
            return this;
        }
    }

    // Serializer fast-path. The hot indexer overloads leave `Children` holding a `Component?[]`.
    // Exposing the raw array lets the render walk iterate by index instead of `foreach`-ing the
    // `IEnumerable<Component?>` interface — which boxes a `SZGenericArrayEnumerator<Component?>`
    // (~32 B) per child-bearing element, every render. Returns null for the List/LINQ-pipeline
    // backings, which fall back to the virtual `RenderChildren()` walk. No component in Rask.Core
    // overrides `RenderChildren`, so for Element subclasses this array is exactly what it yields.
    internal Component?[]? ChildrenArray => Children as Component?[];

    // Null TagName means "not an HTML element" (Fragment/Doctype/Text/Raw/ErrorBoundary/user
    // components). When non-null, HtmlSerializer wraps WriteAttributes(sb)/RenderChildren()
    // output in `<tag>…</tag>` (or self-closes when SelfClosing is true).
    protected virtual string? TagName => null;
    protected virtual bool SelfClosing => false;

    internal string? TagNameInternal => TagName;
    internal bool SelfClosingInternal => SelfClosing;

    // Nearest enclosing ErrorBoundary, stamped during the render walk (HtmlSerializer
    // default branch). Async lifecycle continuations + dispatcher catch sites consult this
    // pointer to trip the right boundary; null means no ancestor boundary registered. Hoisted
    // into LiveState — only user components get one stamped, and the null-guard keeps a
    // boundaryless component (or a plain Element) from allocating a LiveState just to store null.
    internal ErrorBoundary? Boundary
    {
        get => _live?.Boundary;
        set
        {
            if (value is null && _live is null)
            {
                return;
            }

            Live.Boundary = value;
        }
    }

    // Components that read mutable state the framework doesn't observe (e.g. RouteState in
    // Router/Outlet) must opt out of render caching: without this their cached subtree gets
    // reused even after the global state changed. User code should set internal state +
    // call StateHasChanged() instead — only opt in if you genuinely cannot.
    protected virtual bool BypassRenderCache => false;

    // Backing store for Element.Ref, hoisted into the lazy LiveState so a plain element (the
    // overwhelming majority — refs are opt-in) keeps `_live` null and pays nothing for the
    // feature. The setter only forces a LiveState allocation when an actual ref is assigned;
    // setting `default` on a ref-less element is a no-op.
    internal ElementRef? ElementRefInternal
    {
        get => _live?.ElementRef;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.ElementRef = value;
            }
        }
    }

    // Backing store for Element.Role/TabIndex/Aria, hoisted into the lazy LiveState for the same
    // reason as Ref: accessibility attributes are opt-in and rare, so a plain element keeps `_live`
    // null and adds zero footprint. The setters only force a LiveState allocation when an actual
    // value is assigned; setting null on an element that never used the feature is a no-op.
    internal string? RoleInternal
    {
        get => _live?.Role;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.Role = value;
            }
        }
    }

    internal int? TabIndexInternal
    {
        get => _live?.TabIndex;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.TabIndex = value;
            }
        }
    }

    internal IReadOnlyDictionary<string, string?>? AriaInternal
    {
        get => _live?.Aria;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.Aria = value;
            }
        }
    }

    // Backing store for Element.Attributes, on the same terms as AriaInternal: reading never allocates,
    // and writing only forces the LiveState into existence once a value is actually assigned.
    internal IReadOnlyDictionary<string, string?>? ExtraAttrsInternal
    {
        get => _live?.ExtraAttrs;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.ExtraAttrs = value;
            }
        }
    }

    // The rest of HTML's global attributes (#693), on a side object rather than a typed field each.
    // The reasoning is the one LiveState.ExtraAttrs records: LiveState is allocated per node on a
    // mounted page, so every field on it costs ~8 B on every node of every live session (~56 KB per
    // field on a 1,000-row page). Six typed fields would be ~336 KB there; one reference is ~56 KB,
    // and only an element that actually names one of these attributes allocates the side object.
    //
    // Reads go through `_live?.Globals?.X`, so an element that names none pays two null checks and no
    // allocation — and Element.WriteAttributes fetches the object ONCE via GlobalAttrsInternal rather
    // than re-walking it per attribute, which makes the common element cheaper than a field each.
    internal sealed class GlobalAttrs
    {
        public string? Lang;
        public string? Dir;
        public string? Popover;
        public string? ContentEditable;
        public bool? Spellcheck;
        public bool? Translate;
    }

    // The whole side object in one read, for the writer's single null check.
    internal GlobalAttrs? GlobalAttrsInternal => _live?.Globals;

    private GlobalAttrs Globals => Live.Globals ??= new GlobalAttrs();

    // Each setter mirrors the Role/Aria shape: assigning null to an element that never set one is a
    // no-op, so neither the LiveState nor the side object is forced into existence by a null write.
    internal string? LangInternal
    {
        get => _live?.Globals?.Lang;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Lang = value;
            }
        }
    }

    internal string? DirInternal
    {
        get => _live?.Globals?.Dir;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Dir = value;
            }
        }
    }

    internal string? PopoverInternal
    {
        get => _live?.Globals?.Popover;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Popover = value;
            }
        }
    }

    internal string? ContentEditableInternal
    {
        get => _live?.Globals?.ContentEditable;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.ContentEditable = value;
            }
        }
    }

    internal bool? SpellcheckInternal
    {
        get => _live?.Globals?.Spellcheck;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Spellcheck = value;
            }
        }
    }

    internal bool? TranslateInternal
    {
        get => _live?.Globals?.Translate;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Translate = value;
            }
        }
    }

    /// <summary>
    ///     A <see cref="System.Threading.CancellationToken" /> for this component's cancellable async
    ///     work. It is cancelled when the component is unmounted (navigation away, parent removed, or
    ///     session teardown) — and, while an event handler is running, <em>also</em> when the host
    ///     cancels that dispatch: a server-side <c>RaskServerOptions.HandlerTimeout</c> elapsing, or the
    ///     WebSocket closing. Pass it into the <c>HttpClient</c> calls, <c>Task.Delay</c>s, and other
    ///     cancellable work an <c>OnClick</c> / <c>OnSubmit</c> handler or a lifecycle hook starts, so the
    ///     work aborts when the component goes away and a slow handler unwinds instead of pinning the
    ///     session's render pipeline. In a lifecycle hook (no handler dispatch) it is just the lifetime
    ///     token. Cancellation is cooperative — synchronous or token-ignoring handler code cannot be
    ///     forcibly aborted; a handler must observe the token to be cancelled.
    /// </summary>
    protected CancellationToken CancellationToken
    {
        get
        {
            // While an event handler runs, the dispatch scope holds a token already linked with this
            // component's lifetime token (see TryInvokeHandlerAsync); outside one it is the raw lifetime
            // token. The scope is only pushed when a handler timeout is configured, so the common path
            // is the plain lifetime token below.
            var dispatch = DispatchEventTokenScope.Current;
            return dispatch.CanBeCanceled ? dispatch : LifetimeToken;
        }
    }

    // The raw, stable lifetime token — cancelled once on unmount. Used internally to seed the linked
    // per-dispatch token without re-entering the context-aware CancellationToken getter above.
    private CancellationToken LifetimeToken =>
        LazyInitializer.EnsureInitialized(ref Live.LifetimeCts, () => new CancellationTokenSource()).Token;

    // Hoisted into LiveState like Boundary: set only on the live-render root and on GetOrCreate'd
    // user components (both of which already carry a LiveState); the null-guard keeps a `?? =` with a
    // null handle, or a plain Element, from allocating one.
    internal IRenderHandle? RenderHandle
    {
        get => _live?.RenderHandle;
        set
        {
            if (value is null && _live is null)
            {
                return;
            }

            Live.RenderHandle = value;
        }
    }

    internal IReadOnlyDictionary<(Type, int), Component> PersistedChildren => _live?.Children ?? _emptyChildren;

    /// <summary>
    ///     Override to declare resources this component needs in the page <c>&lt;head&gt;</c>
    ///     (stylesheets, scripts, meta tags, the document title). The framework collects the
    ///     output from every component currently in the tree, dedupes top-level children by
    ///     their rendered HTML, and substitutes the result for the
    ///     <c>Generated.RaskHeadAssets</c> placeholder. When a component goes away on
    ///     a subsequent render, its head contribution drops out automatically — the registry
    ///     is rebuilt from scratch each pass.
    ///     <para>
    ///         Default is <c>null</c> — no head contribution. Typical override returns a collection
    ///         expression of <c>Link</c> / <c>Script</c> / <c>Title</c> / <c>Meta</c> calls (e.g.
    ///         <c>HeadAssets =&gt; [Title(...), Meta(...)]</c>) or a single tag. Return <c>null</c> for
    ///         "no contribution" (including conditional bodies:
    ///         <c>HeadAssets =&gt; cond ? [Title(...)] : null</c>).
    ///     </para>
    /// </summary>
    protected virtual Component? HeadAssets => null;

    /// <summary>
    ///     This component's <see cref="HeadAssets" /> contribution as its last render produced it, or
    ///     <c>null</c> when it has none.
    /// </summary>
    /// <remarks>
    ///     Read rather than re-evaluated, by both the serializer's collection point and the
    ///     clean-subtree cache: <see cref="HeadAssets" /> is a user-written expression that builds components,
    ///     so it has to run exactly once per render, inside the render that owns what it builds (see
    ///     <see cref="RenderForLive" />).
    /// </remarks>
    internal Component? CachedHeadInternal => _live?.CachedHead;
    internal void MarkReadsAmbientStateInternal() => SetFlag(FlagReadsAmbientState, true);

    // Test seam: whether this component read something the render cache cannot see (context,
    // edit context, culture). Paired with the setter above so a test can assert the marking
    // happened without reaching into the flag bits.
    internal bool ReadsAmbientStateInternal => _readsAmbientState;

    /// <summary>
    ///     The <c>lang</c> attribute for the document's <c>&lt;html&gt;</c> element. Read off the root
    ///     component only — Rask builds the shell around whatever the root renders. Return <c>null</c> to
    ///     emit no <c>lang</c> at all.
    /// </summary>
    /// <remarks>
    ///     Follows the session's language once the app configures cultures, and is otherwise exactly
    ///     <c>"en"</c> as before. The fallback is literal rather than "whatever the machine's locale
    ///     says" on purpose: reporting the ambient culture would turn <c>lang="en"</c> into
    ///     <c>lang="en-US"</c> on any US-locale machine and change the HTML of every app that never
    ///     asked for localization.
    /// </remarks>
    protected virtual string? HtmlLang => Globalization.RaskCulture.HtmlLang ?? "en";

    /// <summary>
    ///     The <c>dir</c> attribute for the document's <c>&lt;html&gt;</c> element. Defaults to
    ///     <c>"rtl"</c> for a right-to-left session culture and <c>null</c> otherwise, which emits no
    ///     attribute at all — left-to-right is HTML's own default, so writing it would only add bytes
    ///     to every page.
    /// </summary>
    protected virtual string? HtmlDir => Globalization.RaskCulture.HtmlDir;

    /// <summary>
    ///     The <c>class</c> attribute for the document's <c>&lt;body&gt;</c> element (theming hooks,
    ///     Bootstrap ground classes). Read off the root component only. Default <c>null</c> — no class.
    /// </summary>
    protected virtual string? BodyClass => null;

    /// <summary>
    ///     Composes the document around the framework-built <c>&lt;head&gt;</c> and this component's
    ///     rendered body. Override on the root component when <see cref="HtmlLang" /> and
    ///     <see cref="BodyClass" /> are not enough — an extra attribute on <c>&lt;html&gt;</c>, an
    ///     element wrapped around the app, a wholly hand-built document:
    ///     <code>
    ///     protected override Component Shell(Component head, Component body) =>
    ///         Html("en", Dir: "rtl")[head, Body(Class: "dark")[body]];
    ///     </code>
    ///     <para>
    ///         The pieces arrive as <b>parameters</b> — <paramref name="head" /> is the framework's
    ///         <c>&lt;head&gt;</c> element, which collects every mounted component's <see cref="HeadAssets" />
    ///         contribution plus the scoped CSS/JS assets, and <paramref name="body" /> is the app's own
    ///         render output. Place both; dropping <paramref name="head" /> loses every head asset on the
    ///         page, and dropping <paramref name="body" /> renders nothing. The doctype is emitted by the
    ///         framework ahead of whatever this returns, and the runtime <c>&lt;script&gt;</c> is appended
    ///         inside <c>&lt;body&gt;</c> automatically.
    ///     </para>
    ///     <para>
    ///         Evaluated once per render, <b>before</b> the app's own <see cref="Render" /> runs, so it
    ///         cannot observe state that render produces. Anything reactive belongs in the body or in
    ///         <see cref="HeadAssets" />.
    ///     </para>
    /// </summary>
    protected virtual Component Shell(Component head, Component body) =>
        Html.Lang(HtmlLang).Dir(HtmlDir)[head, Body.Class(BodyClass)[body]];

    // The host's entry into the escape hatch above: RootErrorBoundary composes the document around the
    // App, so it needs to reach the App's override. Kept internal because Shell is a user-facing
    // extension point, not a call site — nothing outside the framework composes a document root.
    internal Component ShellInternal(Component head, Component body) => Shell(head, body);

    /// <summary>
    ///     How this component is rendered and transported — see <see cref="RenderEngine" />. Constant for the
    ///     session, so branching a <see cref="Render" /> on it is render-cache safe. See
    ///     <see cref="IsServer" /> / <see cref="IsWasm" />.
    /// </summary>
    protected RenderEngine HostEngine => LiveRenderContext.CurrentSync?.Engine ?? RenderEngine.Server;

    /// <summary>
    ///     The culture to format dates, numbers and currency with for the visitor rendering this
    ///     component.
    /// </summary>
    /// <remarks>
    ///     Unlike <see cref="HostEngine" />, which is fixed for the session, culture can change while the
    ///     app is running — so reading it marks this component as depending on ambient state, which opts
    ///     its subtree out of the clean-subtree render cache. Without that, switching language would
    ///     leave cached subtrees on screen still rendered in the old one.
    /// </remarks>
    protected System.Globalization.CultureInfo Culture => Globalization.RaskCulture.Current;

    /// <summary>The language to render this component's text in. Marks ambient state, as <see cref="Culture" /> does.</summary>
    protected System.Globalization.CultureInfo UICulture => Globalization.RaskCulture.CurrentUI;

    /// <summary>Whether the visitor's language is written right-to-left. Marks ambient state.</summary>
    protected bool IsRightToLeft => Globalization.RaskCulture.IsRightToLeft;

    /// <summary><c>true</c> when rendered server-side over a live connection (<see cref="RenderEngine.Server" />).</summary>
    protected bool IsServer => HostEngine == RenderEngine.Server;

    /// <summary><c>true</c> when rendered in the browser WebAssembly runtime (<see cref="RenderEngine.Wasm" />).</summary>
    protected bool IsWasm => HostEngine == RenderEngine.Wasm;

    internal void WriteAttributesInternal(StringBuilder sb) => WriteAttributes(sb);
    internal IEnumerable<Component?> RenderChildrenInternal() => RenderChildren();
    internal IDisposable? EnterChildrenScopeInternal() => EnterChildrenScope();

    // Default: no HTML attributes. HTML element subclasses derive from Element, which
    // overrides this to emit id/class/style/data-*. Tag-specific overrides chain via
    // `base.WriteAttributes(sb)` so the universal attrs lead and tag-specific attrs follow.
    // Direct StringBuilder writes avoid the per-attribute KeyValuePair + iterator state-machine
    // allocations that the previous IEnumerable<KVP> shape forced on every render.
    protected virtual void WriteAttributes(StringBuilder sb) { }

    // Emit one attribute with the standard space prefix. Null value → bare attribute
    // (e.g. `required`, `disabled`); non-null → name="encoded-value" with full HTML escaping
    // matching the prior HtmlSerializer behaviour. Fast-paths plain ASCII values through
    // HtmlSerializer.AppendEncoded so encoder-no-op cases skip the allocation.
    protected static void AppendAttr(StringBuilder sb, string name, string? value)
    {
        sb.Append(' ').Append(name);
        if (value is not null)
        {
            sb.Append("=\"");
            HtmlSerializer.AppendEncoded(sb, value);
            sb.Append('"');
        }

        FrameSinkScope.Current?.Attribute(name, value);
    }

    // Overload that writes a two-part attribute name directly without allocating an
    // intermediate concatenation. Used by Element for `data-{key}` — `"data-" + kv.Key`
    // would otherwise allocate a string per data-attribute per render.
    protected static void AppendAttr(StringBuilder sb, string namePrefix, string nameSuffix, string? value)
    {
        sb.Append(' ').Append(namePrefix).Append(nameSuffix);
        if (value is not null)
        {
            sb.Append("=\"");
            HtmlSerializer.AppendEncoded(sb, value);
            sb.Append('"');
        }

        if (FrameSinkScope.Current is { } fw)
        {
            // Only allocate the concatenated name when a frame writer is active. The
            // common no-frames path stays zero-allocation.
            fw.Attribute(namePrefix + nameSuffix, value);
        }
    }

    // Integer-valued attribute (e.g. tabindex). Formats the value straight into the builder via
    // a stack buffer, so the no-frames render path allocates nothing — int.ToString() would
    // allocate a string per element on every render. An int's text is always HTML-safe (digits
    // plus an optional leading minus), so it skips the encode pass.
    protected static void AppendAttr(StringBuilder sb, string name, int value)
    {
        sb.Append(' ').Append(name).Append("=\"");
        Span<char> buffer = stackalloc char[12]; // int.MinValue is 11 chars; 12 always fits.
        _ = value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture);
        sb.Append(buffer[..written]);
        sb.Append('"');

        if (FrameSinkScope.Current is { } fw)
        {
            // Allocate the value string only when a frame writer is active.
            fw.Attribute(name, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    // URL-bearing attribute (href/cite/action and iframe/script/object sources). Scheme is
    // sanitized by default — javascript:/vbscript:/data: are neutralized to about:blank — to
    // close the DOM-XSS hole that plain HTML-encoding leaves open. Wrap a trusted value in
    // RaskUrl.Trusted(...) to opt out. Otherwise identical to AppendAttr (incl. frame sink).
    protected static void AppendUrlAttr(StringBuilder sb, string name, string? value)
    {
        AppendAttr(sb, name, UrlSanitizer.Sanitize(value));
    }

    // Media URL attribute (img/audio/video/source src, poster). As AppendUrlAttr but also
    // allows data:image/*, data:video/*, data:audio/* (inline media is common and inert here).
    protected static void AppendMediaUrlAttr(StringBuilder sb, string name, string? value)
    {
        AppendAttr(sb, name, UrlSanitizer.SanitizeMedia(value));
    }

    protected virtual IEnumerable<Component?> RenderChildren() => Children ?? [];

    // Tag components override this to wrap children rendering in an ambient scope
    // (e.g. Form pushes an EditContext for descendant fields to consume).
    protected virtual IDisposable? EnterChildrenScope() => null;

    // Test seam: used by ReconciliationTests to inject a "previous render" snapshot
    // for this component before a render begins.
    internal void SeedPreviousChildren(Dictionary<(Type, int), Component> previous) =>
        Live.PreviousChildren = previous;

    // Override to produce this component's subtree. Returns a single component or a `[...]`
    // collection expression (Component is itself a collection-expression target). The base returns
    // the component itself; return `null` to render nothing. Symmetric with `Head`.
    protected virtual Component? Render() => this;

    /// <summary>
    /// Renders this component and its subtree to a standalone HTML string — a one-shot,
    /// static render with no live-update wiring. Uses a pooled <see cref="System.Text.StringBuilder"/>.
    /// </summary>
    /// <returns>The serialized HTML for this component's subtree.</returns>
    public string ToHtml()
    {
        // Rent a StringBuilder from the shared pool instead of allocating per call. The
        // pool returns it on dispose; oversized buffers (>64 KiB) are discarded so a single
        // huge render doesn't retain an outlier capacity indefinitely.
        var sb = RaskStringBuilderPool.Shared.Get();

        // HeadSentinelIndex is a byte offset into whichever builder is being serialized, and this one is
        // not the page — it is a private buffer whose string is handed back to the caller, never spliced.
        // Left unguarded, a component that calls ToHtml() on a tree containing a <head> (the documented
        // way to demo the document elements, which cannot render live inside a page) publishes an offset
        // into THIS buffer, and RenderAsLiveRoot then splices the head-asset block there — into the middle
        // of whatever the real page had at that position. A page with its own <head> is safe by accident,
        // since recording is first-wins and the shell's head goes first; a render without one — every
        // RaskTest.Render, so every unit test — is not. See #627.
        // CurrentSync, not Current: it is the accessor HtmlSerializer writes HeadSentinelIndex through
        // (HtmlSerializer.cs, the <head> branch), so it names exactly the context at risk — and it is the
        // cheap ThreadStatic rather than an AsyncLocal read, which matters because HeadAssetRegistry.Add
        // routes every head contribution through ToHtml() during the render walk.
        var live = LiveRenderContext.CurrentSync;
        var savedSentinel = live?.HeadSentinelIndex ?? -1;

        try
        {
            HtmlSerializer.Serialize(this, sb);
            return sb.ToString();
        }
        finally
        {
            if (live is not null)
            {
                live.HeadSentinelIndex = savedSentinel;
            }

            RaskStringBuilderPool.Shared.Return(sb);
        }
    }

    protected virtual void OnMount() { }
    protected virtual Task OnMountAsync() => Task.CompletedTask;
    protected virtual void OnPropsChanged() { }
    protected virtual Task OnPropsChangedAsync() => Task.CompletedTask;
    protected virtual void OnRendered(bool firstRender) { }
    protected virtual Task OnRenderedAsync(bool firstRender) => Task.CompletedTask;

    /// <summary>
    ///     Runs once when this component is removed from the tree — navigation away, parent
    ///     subtree torn down, or session disposal. Symmetric with <see cref="OnMount" />.
    ///     The component's <see cref="CancellationToken" /> is still live here; it is
    ///     cancelled immediately after this hook returns. Do not call
    ///     <see cref="StateHasChanged" /> from inside — the component is leaving the tree.
    /// </summary>
    protected virtual void OnUnmount() { }

    /// <summary>
    ///     Async counterpart to <see cref="OnUnmount" />. Awaited on async disposal paths
    ///     (e.g. <c>LiveSession.DisposeAsync</c>), fire-and-forget with fault logging on
    ///     synchronous disposal — mirroring the framework's handling of
    ///     <see cref="IAsyncDisposable" />.
    /// </summary>
    protected virtual Task OnUnmountAsync() => Task.CompletedTask;

    /// <summary>
    ///     Whether this component has left the tree. Read by <c>QuiescenceScope</c> so a server
    ///     render stops waiting on work whose owner was unmounted mid-pass.
    /// </summary>
    internal bool IsUnmountedInternal => Live.IsUnmounted;

    /// <summary>
    ///     Whether the last render walk rooted here mounted <paramref name="type" />. The set is
    ///     cleared at the top of every walk and populated as components mount, so this describes
    ///     the render whose HTML is current — not the tree's history.
    /// </summary>
    internal bool MountedTypeInLastRender(Type type) =>
        Live.MountedTypes?.Contains(type) == true;

    internal void RaiseLifecycleBeforeRender(bool propsChanged)
    {
        var firstRender = !Live.HasInitialized;
        if (firstRender)
        {
            Live.HasInitialized = true;
            OnMount();
            InvokeAsyncLifecycleWithRendering(OnMountAsync);
        }

        if (firstRender || propsChanged)
        {
            Live.PropsDirty = true;
            OnPropsChanged();
            InvokeAsyncLifecycleWithRendering(OnPropsChangedAsync);
        }
    }

    internal void RaiseOnRendered(bool publishOnly = false)
    {
        // publishOnly: this is the render walk triggered by a previous OnRenderedAsync
        // continuation's auto-rerender. Skip OnRendered / OnRenderedAsync on components
        // that already rendered at least once — re-entering the hook would re-await
        // whatever it awaits (e.g. js.InvokeVoidAsync), enqueue another pending task,
        // schedule another publish render, complete → loop. First-time renders still
        // fire so newly-mounted components on the same walk get their first
        // OnRendered(firstRender:true) — they don't have a prior continuation in flight,
        // so they can't loop.
        if (publishOnly && Live.HasRenderedOnce)
        {
            return;
        }

        var firstRender = !Live.HasRenderedOnce;
        Live.HasRenderedOnce = true;
        OnRendered(firstRender);

        var task = OnRenderedAsync(firstRender);
        if (task.IsCompleted)
        {
            if (task.IsFaulted)
            {
                ReportLifecycleFault(this, task.Exception);
            }

            return;
        }

        // Auto-rerender on continuation completion so users get OnMountAsync-style
        // "mutate state after the await and it paints" without explicit StateHasChanged.
        // RequestPublishRenderAsync flags the resulting walk as publishOnly so the
        // publish render skips this same hook on every already-rendered component (see
        // top of method). Without that flag, multi-component trees cascade infinitely:
        // A's publish render fires B's OnRenderedAsync, B's continuation publishes,
        // which fires A's OnRenderedAsync again, ad infinitum.
        task.ContinueWith(static (t, state) =>
        {
            var comp = (Component)state!;
            if (t.IsFaulted)
            {
                ReportLifecycleFault(comp, t.Exception);
                return;
            }

            if (t.IsCanceled)
            {
                return;
            }

            if (comp.Live.IsUnmounted)
            {
                return;
            }

            var handle = comp.RenderHandle;
            if (handle is null)
            {
                return;
            }

            comp.Live.StateDirty = true;
            _ = handle.RequestPublishRenderAsync();
        }, this, TaskContinuationOptions.ExecuteSynchronously);
    }

    // One-shot guard for the unmount → cancel → dispose teardown. A tree mutation inside an
    // OnUnmount hook (e.g. clearing PersistedChildren, or re-parenting) can leave a node
    // reachable from more than one dispose pass; without this guard that node would fire
    // OnUnmount and the user's Dispose twice. Returns true exactly once. The lifetime CTS is
    // already idempotent (DisposeLifetimeToken nulls it via Interlocked, Cancel swallows ODE);
    // this protects the user-visible lifecycle hooks. Disposal runs under the session render
    // lock, so a plain flag is sufficient — same threading contract as IsUnmounted.
    internal bool TryBeginDispose()
    {
        if (Live.IsDisposed)
        {
            return false;
        }

        Live.IsDisposed = true;
        return true;
    }

    internal void CancelLifetimeToken()
    {
        // No LiveState → LifetimeToken was never accessed → no CTS to cancel (plain Elements never
        // reach here). Read the ref off LiveState only once we know it exists.
        var cts = _live is null ? null : Volatile.Read(ref _live.LifetimeCts);
        if (cts is null)
        {
            return;
        }

        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    internal void DisposeLifetimeToken()
    {
        var cts = _live is null ? null : Interlocked.Exchange(ref _live.LifetimeCts, null);
        cts?.Dispose();
    }

    // Returns null when there's nothing for the caller to await — the async hook either
    // wasn't overridden, completed synchronously, or already failed (faults logged inline).
    // The sync dispose path fire-and-forgets a non-null return via ObserveUnmountFault;
    // the async path awaits it directly. Skipped entirely when Live.HasInitialized is false —
    // a component that never mounted has no unmount counterpart, symmetric with OnMount.
    internal Task? RaiseUnmount()
    {
        if (!Live.HasInitialized)
        {
            return null;
        }

        // Set BEFORE OnUnmount fires so any StateHasChanged inside the hook (or
        // from in-flight async work — LifecycleSyncContext continuations from a
        // long-running OnMountAsync — that settles during/after unmount) is
        // silently swallowed instead of queuing ghost session renders against a
        // disposed component. Matches the documented "StateHasChanged() inside
        // OnUnmount is a no-op" contract.
        Live.IsUnmounted = true;

        try { OnUnmount(); }
        catch (Exception ex) { LogUnmountError(this, ex); }

        Task task;
        try { task = OnUnmountAsync(); }
        catch (Exception ex)
        {
            LogUnmountError(this, ex);
            return null;
        }

        if (task.IsCompletedSuccessfully)
        {
            return null;
        }

        if (task.IsFaulted)
        {
            LogUnmountError(this, (Exception?)task.Exception?.InnerException ?? task.Exception!);
            return null;
        }

        if (task.IsCanceled)
        {
            return null;
        }

        return task;
    }

    internal static void LogUnmountError(Component comp, Exception ex) =>
        RaskDiagnostics.Report(
            RaskLogLevel.Error,
            "Rask.Lifecycle",
            $"Rask unmount hook on {comp.GetType().Name} threw",
            ex);

    private void InvokeAsyncLifecycleWithRendering(Func<Task> invoke)
    {
        var prev = SynchronizationContext.Current;
        var ctx = new LifecycleSyncContext(this);
        SynchronizationContext.SetSynchronizationContext(ctx);
        Task task;
        try { task = invoke(); }
        finally { SynchronizationContext.SetSynchronizationContext(prev); }

        if (task.IsCompleted)
        {
            if (task.IsFaulted)
            {
                ReportLifecycleFault(this, task.Exception);
            }

            return;
        }

        // Registered BEFORE the terminal ContinueWith below, and the order is load-bearing: that
        // continuation runs ExecuteSynchronously and can fire inline the moment the hook completes,
        // so registering afterwards would let a fast hook finish first and leave the tracker
        // reporting a render that had already settled when it had not.
        QuiescenceScope.Current?.Track(task, this);

        // LifecycleSyncContext renders after each in-method await. The terminal render
        // here is the fallback for hooks that return a Task without awaiting it AND for
        // ConfigureAwait(false)-only chains where Post never fires. When the user's last
        // statement IS an await (the common case), Post already fired StateHasChanged
        // for it — and the user's method body returns inside d(state), transitioning the
        // task to Completed while still inside the Post lambda. ExecuteSynchronously
        // would then fire THIS callback inline before Post's own StateHasChanged runs,
        // producing two renders back-to-back. ctx.PostFired lets us short-circuit in
        // that case.
        task.ContinueWith(static (t, state) =>
        {
            var (comp, ctx) = ((Component, LifecycleSyncContext))state!;
            if (t.IsFaulted)
            {
                ReportLifecycleFault(comp, t.Exception);
                return;
            }

            if (t.IsCanceled)
            {
                return;
            }

            if (ctx.PostFired)
            {
                return;
            }

            comp.StateHasChanged();
        }, (this, ctx), TaskContinuationOptions.ExecuteSynchronously);
    }

    private static ErrorBoundary? ResolveHandlerBoundary(Component owner) =>
        owner as ErrorBoundary ?? owner.Boundary;

    private static void ReportLifecycleFault(Component comp, AggregateException? ex)
    {
        var actual = ex?.InnerException ?? ex;
        if (actual is null)
        {
            return;
        }

        // Prefer the boundary: it'll re-render with the fallback. Fall back to a diagnostics
        // report only when there is no ancestor boundary, so a faulting hook is never silent.
        var boundary = comp.Boundary;
        if (boundary is not null)
        {
            boundary.Trip(actual, ErrorSource.Lifecycle);
            return;
        }

        RaskDiagnostics.Report(
            RaskLogLevel.Error,
            "Rask.Lifecycle",
            $"Rask lifecycle hook on {comp.GetType().Name} faulted",
            actual);
    }

    internal Component? RenderForLive()
    {
        // Skip when nothing meaningful changed: no first-time render, no prop change, no
        // explicit StateHasChanged, and the component hasn't opted out of caching. The
        // serializer still walks Live.CachedRenderResult, so any descendant whose own
        // Live.StateDirty or Live.PropsDirty IS set will re-render itself — ancestors don't need to
        // re-execute to permit that.
        //
        // A component that renders nothing (Render() returns null) leaves CachedRenderResult null,
        // so it never hits this cache and re-runs its (trivial) Render() on each non-dirty walk.
        // That's fine: nothing-render is state-driven — such components set StateDirty when they
        // gain content — and null can't double as the "already rendered" sentinel.
        //
        // A non-Element component that has children cannot reuse its cache: its children arrive via
        // the `[...]` indexer (not a factory param, so absent from the prop-change check) and are
        // BAKED INTO its Render() output, so a changed child set — e.g. a conditional alert appearing
        // — would be silently dropped. Elements are exempt: their children are walked at serialization
        // time (RenderChildren), never embedded in the cached result, so the cache stays valid. This is
        // what lets composite wrappers (a Bs* card around dynamic content) behave like the inline
        // elements they replace without opting out of caching by hand.
        if (Live.CachedRenderResult is not null && !Live.PropsDirty && !Live.StateDirty
            && !BypassRenderCache && !_readsAmbientState
            && (Children is null || this is Element))
        {
            return Live.CachedRenderResult;
        }

        // Swap the two dictionaries instead of allocating a fresh map per render —
        // both fields persist across the component lifetime, so after first render
        // every subsequent render reuses the same two buffers. _children is cleared
        // before any new writes; Live.PreviousChildren retains the prior frame's entries
        // for GetOrCreateChild's reuse lookup. If this component has never had child
        // GetOrCreate calls (most Elements), both dicts stay null and the swap is a
        // no-op — GetOrCreateChild lazily allocates on first write.
        if (_live?.Children is not null)
        {
            // First-time swap: allocate the back buffer so the persistent two-dict pool
            // works steady-state. Subsequent renders just swap and Clear — no allocation.
            _live.PreviousChildren ??= new Dictionary<(Type, int), Component>();
            (_live.PreviousChildren, _live.Children) = (_live.Children, _live.PreviousChildren);
            _live.Children.Clear();
        }

        // The keyed map rotates on exactly the same discipline, and only for a parent that keys a child
        // at all — otherwise both stay null and this is two null checks (#685).
        if (_live?.KeyedChildren is not null)
        {
            _live.PreviousKeyedChildren ??= new Dictionary<(Type, object), Component>();
            (_live.PreviousKeyedChildren, _live.KeyedChildren) = (_live.KeyedChildren, _live.PreviousKeyedChildren);
            _live.KeyedChildren.Clear();
        }

        Live.ChildPositions = 0;

        // HtmlSerializer wraps every user-component serialization in an EnterParentScope so
        // the scope is live during BOTH Render() and the walk of its returned subtree —
        // factories inside Render and handlers registered on elements deep in the tree both
        // attribute back to this component.
        //
        // A Render() that THROWS is a supported path — an ancestor ErrorBoundary catches it and
        // re-renders a fallback — so the entries it built before the throw still have to be swept off
        // the per-thread slot stack, which is only ever popped by the render that pushed onto it. A
        // stranded slot pins a live subtree on a pooled thread AND corrupts the next successful render
        // of this same component: that render pushes a second slot for the same target (positional
        // identity hands back the same instance), the stale one drains first, and its stale pending
        // mask blanks a prop the new chain has just set. Only the reset half runs on the way out —
        // firing lifecycle while an exception unwinds could throw again and swallow the original fault,
        // and it would be notifying a render that never happened.
        try
        {
            Live.CachedRenderResult = Render();
        }
        catch
        {
            if (Live.HasEntryChildren)
            {
                Live.HasEntryChildren = false;
                BuilderRuntime.DrainSlots(this);
            }

            throw;
        }

        // The Head override is part of THIS component's render, not of the walk that serializes it.
        // Evaluating it here rather than at the serializer's collection point — which runs in the
        // ENCLOSING component's parent scope, after that component's own render has finished and
        // drained — is what gives an entry inside a Head override the right owner: its identity comes
        // from this component's positional child map (counted on from the render's own children, since
        // the reset above already ran), its pending reset drains with the rest below rather than a
        // frame late, and a Context read inside a Head marks THIS component as ambient-reading.
        //
        // Cached alongside the render result because the registry that collects it is rebuilt every
        // frame while this component may be served from the render cache: re-running the chain on a
        // cache hit would hand out fresh positional identities on every frame (the counter is only
        // reset by a real render), and re-running it at all is work a clean component does not owe.
        Live.CachedHead = HeadAssets;

        // Builder-surface commit point. A generated FACTORY assigns every prop and then calls
        // NotifyParameters itself, because it knows when the props are done. A setter chain has no
        // natural end — `Div.Class("a").Id("b")` could take another setter or the `[...]` indexer — so
        // the entries defer that half to here: the moment Render() returns, every chain it built is
        // complete and nothing can touch those props again before the walk reaches them.
        //
        // This is the exact factory ordering, not an approximation: the factory notifies during the
        // parent's Render(), i.e. with the same ambient state (no Context provider pushed yet, since
        // providers are pushed by the serializer) and always before the child is walked. Which is what
        // makes Live.PropsDirty land in time for RenderForLive's cache check and TryReplayCleanSubtree
        // on the child, and why a child that was built but then dropped from the tree still mounts.
        //
        // Gated on a flag armed by the entries themselves (LiveRenderContext.GetOrCreateEntry), so a
        // tree built entirely from factories never walks the child map here.
        if (Live.HasEntryChildren)
        {
            CommitEntryChildren();
        }

        Live.PropsDirty = false;
        Live.StateDirty = false;
        return Live.CachedRenderResult;
    }

    // Fires the deferred NotifyParameters for every child a builder entry produced during the Render()
    // that just finished. Kept out of RenderForLive so the hot path is a single bool test.
    private void CommitEntryChildren()
    {
        // A commit can build more entries. The hooks it runs are user code, they run while this
        // component is still the one in scope, and a factory or an entry called from one lands here —
        // after the sweep that was supposed to complete them. So the whole pass repeats until it finds
        // nothing new; the flag is armed by the entry itself, so the overwhelmingly common "the hooks
        // built nothing" case costs one more bool test. It terminates on anything the factory survives:
        // a hook that unconditionally builds a component whose hook does the same recurses on the
        // factory too, straight into a stack overflow, because there the notification is synchronous.
        do
        {
            Live.HasEntryChildren = false;

            // Ordering is load-bearing: the pending resets put every prop the chain did NOT name back
            // to the value the factory would have passed, folding each one into EntryPropsChanged as it
            // goes, so they have to run before the commit below reads that flag.
            BuilderRuntime.DrainSlots(this);

            if (_live?.Children is { Count: > 0 } children)
            {
                CommitEach(children);
            }
        }
        while (Live.HasEntryChildren);
    }

    // Fires the commit over a SNAPSHOT of the child map rather than over the map itself. The lifecycle
    // hooks it runs may build components, and building one writes to this very dictionary
    // (GetOrCreateChild) — which, mid-enumeration, is an InvalidOperationException. It was not reachable
    // before the deferred commit existed: the factory notified from inside Render(), where the map is
    // written but never walked.
    //
    // The snapshot rides a per-thread buffer, reused across renders and unwound like a stack, so it
    // allocates nothing in the steady state and stays correct when a hook re-enters another component's
    // render (a nested ToHtml(), which commits onto the same buffer above our range).
    private static void CommitEach(Dictionary<(Type, int), Component> children)
    {
        var buffer = BuilderRuntime.CommitBuffer;
        var start = buffer.Count;
        try
        {
            foreach (var child in children.Values)
            {
                buffer.Add(child);
            }

            var end = buffer.Count;
            for (var i = start; i < end; i++)
            {
                buffer[i].CommitEntry();
            }
        }
        finally
        {
            buffer.RemoveRange(start, buffer.Count - start);
        }
    }

    // The child half of the commit. `!HasInitialized` is the mount signal and needs no flag of its own:
    // a factory-built child was already notified inside Render(), so it is initialized and this is a
    // no-op — RaiseLifecycleBeforeRender(false) on an initialized component does nothing at all.
    private void CommitEntry()
    {
        // No LiveState means the child never reached GetOrCreate (nothing to notify) — the same
        // no-context case in which the factory skips NotifyParameters too.
        if (_live is not { } state || (state.HasInitialized && !state.EntryPropsChanged))
        {
            return;
        }

        var propsChanged = state.EntryPropsChanged;
        state.EntryPropsChanged = false;
        RaiseLifecycleBeforeRender(propsChanged);
    }

    // Armed by LiveRenderContext.GetOrCreateEntry on the component whose Render() is building the tree.
    internal void ArmEntryCommitInternal() => Live.HasEntryChildren = true;

    // Claims the LiveState for an entry-built child that has a lifecycle to run, so the commit below can
    // keep using "no LiveState" to mean "not mine to notify". See GetOrCreateEntry for why the two are
    // not the same question and why a handle-less render is where they came apart.
    internal void EnsureLiveStateInternal() => _ = Live;

    /// <summary>
    ///     Records that a builder setter wrote a value different from the one already on this component,
    ///     so the deferred commit reports <c>propsChanged: true</c>.
    /// </summary>
    /// <remarks>
    ///     The generated setters call this through <see cref="BuilderRuntime" /> (they are emitted into
    ///     the global namespace of every consuming assembly, so the entry point has to be public). It is
    ///     the setter-chain equivalent of the factory's <c>__propsChanged</c> fold: same
    ///     <see cref="EqualityComparer{T}" /> semantics, same exclusions — <c>Key</c>, auto-wrapped
    ///     callbacks and raw delegate props never fold, so the generator simply does not emit
    ///     the call for them.
    /// </remarks>
    internal void MarkEntryPropsChangedInternal() => Live.EntryPropsChanged = true;

    // Phase B clean-subtree frame replay. A user component whose last render was cached as a frame
    // span (pure elements, no handlers, no nested user components — see TryCacheCleanSubtree) re-emits
    // its HTML and frames directly from that span instead of re-walking (and retaining) an Element
    // object graph. Returns true when it replayed; false when the component is dirty or was never
    // cached, in which case the caller walks it normally (re-rendering, and possibly re-caching).
    //
    // The clean test mirrors RenderForLive's short-circuit: no prop/state change, no cache bypass, and
    // no ambient-state read. A dirty component falls through so its fresh Render() runs; if it stays
    // eligible afterwards it re-caches, otherwise it reverts to the element path transparently.
    internal bool TryReplayCleanSubtree(StringBuilder sb, FrameWriter frames, LiveRenderContext? liveCtx)
    {
        var cached = _live?.Cached;
        if (cached is null
            || Live.PropsDirty || Live.StateDirty
            || BypassRenderCache || _readsAmbientState)
        {
            return false;
        }

        // Without a live context there is nowhere to put this subtree's registrations back, and the
        // root's map is cleared every render — replaying would leave its buttons silently dead. So a
        // handler-bearing snapshot needs one; fall through to a walk when there is none.
        if (cached.Handlers is not null && liveCtx is null)
        {
            return false;
        }

        // The captured frames carry a baked-in data-rask-key: this component's own Key forwarded onto
        // its first element, or a keyed ancestor's that our first element adopted. Neither dirties us
        // when it changes — Key is a reconciliation identity, excluded from the propsChanged fold (see
        // ComponentFactoryGenerator), and an ancestor's key was never our prop at all — so a clean
        // component can be sitting on a snapshot whose identity has since gone stale. Replaying it would
        // emit the wrong key and the diff would match this subtree against the wrong sibling, moving the
        // wrong DOM. Fall through to a walk instead: it re-emits under the current key and re-caches.
        //
        // The expression is the identity a walk would emit right now — our own Key when we have one (the
        // serializer arms it, overwriting whatever an ancestor forwarded), else the ancestor's key still
        // pending in the slot. Compared by object rather than by the stringified key: KeyString
        // allocates for a value key (int, Guid) and this runs per keyed node per render, which is
        // exactly the per-update allocation this cache exists to avoid. Object equality is conservative
        // — a Key that changes identity but stringifies the same merely costs a walk — which is the safe
        // direction to be wrong in.
        if (!Equals(Key ?? (object?)KeyForwardScope.Peek(), cached.KeyIdentity))
        {
            return false;
        }

        // Past every bail-out, so the map is only written once the replay is certain to happen — a
        // rejected replay must leave the root's handler state exactly as it found it, or the walk that
        // follows would run against registrations it did not make.
        //
        // The ids to re-register under are this component's own slot ids: settled for its lifetime, and
        // therefore still exactly the ids a walk would issue now no matter what the rest of the page did
        // this frame. That is the whole reason ids are per (component, slot) — there is no page-wide
        // counter left to arrive back at, so an upstream handler count moving no longer costs a re-walk.
        if (cached.Handlers is { } handlers)
        {
            liveCtx!.ReplayHandlerRun(this, handlers);
        }

        // Re-emit the HTML and re-write the full frame stream (with fresh offsets) into the active
        // writer in one pass — the replayed frames are identical to a fresh walk's, so the diff sees
        // no change, and no Element object graph is touched.
        HtmlSerializer.ReplayLeanFrames(cached.Frames.AsSpan(0, cached.FrameCount), sb, frames);

        // Leave the forward slot exactly as a walk would have: empty. A walk either armed our own key
        // and cleared it in its finally, or let our first element consume an ancestor's. Replaying skips
        // both, so an ancestor's key would otherwise stay armed and leak onto the next sibling element.
        KeyForwardScope.Clear();
        return true;
    }

    // Phase B clean-subtree frame capture. Called right after a user component's subtree was walked
    // and serialized into <paramref name="frames" /> (starting at <paramref name="frameStart" />). When
    // the subtree is safe to replay from frames alone, snapshot its frame span and RELEASE the cached
    // Element subtree (CachedRenderResult) so the object graph is collectible — the retained cost drops
    // from the full element tree to a compact frame array. Safety requires:
    //   * no nested user component (a nested component could go dirty independently; replaying the
    //     parent's frames would skip its re-render and show stale content — <paramref name="hadNested" />),
    //   * no indexer Children, no Head contribution (would be dropped on replay), not collecting native
    //     chrome, and cache-eligible (no bypass / ambient-state read) — everything that would make a
    //     frame replay diverge from a walk.
    //
    // A Key does NOT disqualify, though it used to. The key baked into the span (this component's own,
    // or an ancestor's forwarded onto our first element) can change while we stay clean — Key is a
    // reconciliation identity, excluded from the propsChanged fold (see ComponentFactoryGenerator) — so
    // rather than refusing to cache keyed subtrees at all, the snapshot records the identity it was
    // captured under and TryReplayCleanSubtree refuses a replay when it no longer matches.
    //
    // The trade is deliberate and measured, and it is NOT a memory win: caching keyed rows costs ~4%
    // MORE retained memory (a per-row snapshot runs bigger than the small Element graph it releases,
    // ~+266 B/row at 1,000 rows — this cache pays off in bytes when one component snapshots many nodes,
    // not when many components each snapshot a few). It buys a cheaper UPDATE, which is what a user
    // actually feels: the element path re-walks the graph and re-stringifies every Key on every render
    // (a value key allocates — see Component.KeyString), while a replay does neither. On a 1,000-row
    // keyed list that is ~13% less allocation and ~15% less time PER UPDATE, i.e. less GC pressure on
    // every interaction, for a one-off 4% on the retained ceiling. Numbers from the `session-churn`
    // update-cost pass and `session-footprint` (benchmarks/Rask.Benchmarks).
    //
    // <paramref name="forwardedKeyAtCapture" /> is the ambient forwarded key read BEFORE the walk (only
    // meaningful when this component has no Key of its own; ours would overwrite the slot). A KEYLESS
    // component's first element adopts an ancestor's forwarded key, baking someone else's identity into
    // our span — not covered by our own-Key check, and stale-replaying it was a live bug.
    //
    // Event handlers do not disqualify. RenderAsLiveRootCore clears the root's map every render, so a
    // replay that skipped the walk would leave its ids absent from the map — a silently dead button —
    // which is why the snapshot records the handler run and a replay re-registers it. What it does NOT
    // have to reproduce is a position in a page-wide counter: ids belong to (component, slot) and hold
    // for the component's lifetime, so the run goes back under the same ids it came from and nothing
    // upstream can invalidate it.
    internal void TryCacheCleanSubtree(
        FrameWriter frames, int frameStart, bool hadNested,
        string? forwardedKeyAtCapture, LiveRenderContext? liveCtx)
    {
        var count = frames.Count - frameStart;
        if (hadNested
            || Children is not null
            || BypassRenderCache
            || _readsAmbientState
            || CachedHeadInternal is not null
            || count <= 0)
        {
            // This component just walked (first render or a dirty re-render) into something we won't
            // cache — a nested component, a handler, nothing, etc. Any PRIOR snapshot (e.g. this
            // component cached a pure-element "loading" state, then re-rendered into a component-bearing
            // "loaded" state) is now stale, so drop it: otherwise a later clean re-render would replay the
            // outdated subtree and revert the DOM. The element path (CachedRenderResult, set by
            // RenderForLive this walk) stays intact.
            if (_live is not null)
            {
                _live.Cached = null;
            }

            return;
        }

        var span = frames.WrittenSpan.Slice(frameStart, count);

        // Reuse the existing snapshot array when it still fits, so a component that re-renders every
        // frame (e.g. a stateful counter page) re-captures with ZERO allocation — only a fresh or grown
        // subtree allocates. Without this the per-update allocation win regresses by the snapshot size.
        var cached = _live!.Cached ??= new CachedSubtree();
        var snapshot = cached.Frames;
        if (snapshot.Length < count)
        {
            snapshot = new LeanFrame[count];
        }

        // Copy the lean fields; the held snapshot drops the per-render HTML offsets and diff-only
        // component ref (replay regenerates offsets), so it retains ~24 B/node instead of ~40.
        for (var i = 0; i < count; i++)
        {
            ref readonly var f = ref span[i];
            snapshot[i] = new LeanFrame
            {
                Kind = f.Kind,
                Name = f.Name,
                Value = f.Value,
                SubtreeLength = f.SubtreeLength,
                SelfClosing = f.SelfClosing
            };
        }

        cached.Frames = snapshot;
        cached.FrameCount = count;
        // Record the identity this span was captured under so a later replay can prove it is still the
        // right one.
        // Same expression as the replay check, but against the forwarded key as it was BEFORE the walk:
        // by now our first element has consumed it, so the live slot no longer holds it.
        cached.KeyIdentity = Key ?? (object?)forwardedKeyAtCapture;
        // Snapshot the handler run this walk registered (empty run → null, so a handler-free subtree
        // pays nothing and its replay skips re-registration entirely).
        cached.Handlers = liveCtx?.CaptureHandlerRun(this);
        // Drop the Element object graph: a clean re-render now replays the frame span above.
        Live.CachedRenderResult = null;
    }


    // GetOrCreateChild counts positions up from 0, so this can never collide with one.
    private const int AdoptedChildPosition = int.MaxValue;

    /// <summary>
    ///     Registers an already-constructed <paramref name="child" /> as this component's child, outside
    ///     the positional <see cref="GetOrCreateChild{T}" /> path, and gives it a render handle.
    /// </summary>
    /// <remarks>
    ///     For a render root that forwards to a component it did not build through a generated factory —
    ///     which is every component handed to <c>RaskTest.Render</c> as an object rather than produced by
    ///     the factory during the render. Those never reach <c>GetOrCreate</c>, so without adoption they
    ///     serialize but are invisible to the alive-set walk (no <c>OnRendered</c>, no <c>OnUnmount</c>)
    ///     and have no handle to re-render through when an asynchronous lifecycle hook completes.
    ///     <para>
    ///     Deliberately not <see cref="GetOrCreateChild{T}" />: that path's reuse branch clears the
    ///     instance's <see cref="Children" />, which would delete the subtree of a tree built at the call
    ///     site (<c>Div()[Span()]</c>) on its second render, and would put the instance's identity under
    ///     positional-cache rules. Adoption keeps the caller's object exactly as it was handed over.
    ///     </para>
    /// </remarks>
    internal void AdoptChild(Component child, IRenderHandle? handle)
    {
        if (_live?.Children is { } existing)
        {
            foreach (var registered in existing.Values)
            {
                // Already registered this frame — it came from a generated factory's GetOrCreate, which
                // has done both halves of this itself.
                if (ReferenceEquals(registered, child))
                {
                    return;
                }
            }
        }

        child.RenderHandle ??= handle;
        (Live.Children ??= new Dictionary<(Type, int), Component>())[
            (child.GetType(), AdoptedChildPosition)] = child;
    }

    /// <summary>
    ///     Settles which instance a keyed child actually is, replacing the one position handed us.
    /// </summary>
    /// <remarks>
    ///     Called by the <c>Key</c> chain step, on the PARENT, immediately after the entry that built
    ///     <paramref name="provisional" />. `Key` has always been the diff codec's reconciliation identity
    ///     (<c>data-rask-key</c>); until #685 it was not the parent's, so a keyed row's own state — private
    ///     fields, an <c>OnMount</c> subscription — followed its POSITION instead of its item.
    ///     <para>
    ///         The instance the entry handed over is a fresh one (<see cref="GetOrCreateChild{T}" /> stops
    ///         recycling by ordinal once a type is keyed), so claiming an earlier instance discards it.
    ///         That is the cost of correctness here and it is confined to keyed children: an unkeyed tree
    ///         never reaches this method.
    ///     </para>
    /// </remarks>
    internal T ClaimKeyedChild<T>(T provisional, object key) where T : Component
    {
        // From now on this parent identifies T by key rather than by position — see LiveState.KeyedTypes.
        (Live.KeyedTypes ??= new HashSet<Type>()).Add(typeof(T));

        var mapKey = (typeof(T), key);
        var chosen = provisional;
        if (Live.PreviousKeyedChildren is not null
            && Live.PreviousKeyedChildren.TryGetValue(mapKey, out var prev)
            && prev is T kept
            && !ReferenceEquals(kept, provisional))
        {
            chosen = kept;
            // Same reason GetOrCreateChild clears on reuse: children arrive via the `[...]` indexer after
            // the chain, and a childless render must not inherit the last one's subtree.
            chosen.Children = null;
            chosen.RenderHandle ??= provisional.RenderHandle;
        }

        (Live.KeyedChildren ??= new Dictionary<(Type, object), Component>())[mapKey] = chosen;

        // Re-file this frame's positional slot onto whichever instance the key settled on, so the
        // alive-set walk sees the child that is actually rendered here.
        if (Live.Children is not null && Live.LastChildSlot.Type == typeof(T))
        {
            Live.Children[Live.LastChildSlot] = chosen;
        }

        return chosen;
    }

    internal T GetOrCreateChild<T>(
        Func<IServiceProvider, T> factory,
        IServiceProvider? services,
        IRenderHandle? handle) where T : Component
    {
        var key = (typeof(T), Live.ChildPositions++);
        Live.LastChildSlot = key;
        T instance;
        // A type this parent identifies by Key is NOT identified by position (#685): recycling the
        // instance that happens to sit at this ordinal would hand a brand-new key whichever item used
        // to be here, state and all. Create, and let the Key step that follows claim the right one.
        if (Live.KeyedTypes is not null && Live.KeyedTypes.Contains(typeof(T)))
        {
            instance = factory(services!);
            if (instance is Forms.IFormControl newControl)
            {
                Forms.BindingConsumerRegistry.Record(newControl, this);
            }
        }
        else if (Live.PreviousChildren is not null && Live.PreviousChildren.TryGetValue(key, out var prev) &&
                 prev is T t)
        {
            instance = t;
            // The factory re-applies every factory-param property each render, but Children is
            // set by the `[...]` indexer AFTER the factory returns — and a childless element
            // (no indexer) never sets it. Reset it here so a reused instance can't inherit the
            // previous occupant's children. Without this, a structural move that shifts the
            // positional cache onto a former-parent instance (e.g. an empty drop-zone div lands
            // on an old card's slot) keeps that parent's subtree wired in, producing a cyclic
            // tree and a stack overflow when serialized. The indexer overwrites this for any
            // element that does declare children.
            instance.Children = null;
        }
        else
        {
            // Pass through whatever IServiceProvider the LiveRenderContext was given —
            // possibly null. The generated factory closure for non-DI components ignores
            // the parameter, so null is fine; DI-ctor closures (ActivatorUtilities) will
            // surface their own NRE if asked to resolve against a null provider.
            instance = factory(services!);

            // `this` is the creating parent (CurrentParent when the factory ran) — the provider whose
            // Render() authored this control. A form control records it once at creation so a bound
            // two-way write outside a Form can re-render the provider's derived UI (see
            // Forms/BindingConsumerRegistry). The creator is stable across frames, so a reused instance
            // keeps its entry — no work on the steady-state render path.
            if (instance is Forms.IFormControl fc)
            {
                Forms.BindingConsumerRegistry.Record(fc, this);
            }
        }

        instance.RenderHandle ??= handle;
        (Live.Children ??= new Dictionary<(Type, int), Component>())[key] = instance;
        return instance;
    }

    internal Component GetOrCreateChild(
        Type type,
        Func<IServiceProvider, Component> factory,
        IServiceProvider? services,
        IRenderHandle? handle)
    {
        var key = (type, Live.ChildPositions++);
        Component instance;
        if (Live.PreviousChildren is not null && Live.PreviousChildren.TryGetValue(key, out var prev) &&
            prev.GetType() == type)
        {
            instance = prev;
            // See the generic overload: clear children on reuse so a childless element can't
            // inherit a former occupant's subtree after a positional-cache shift.
            instance.Children = null;
        }
        else
        {
            instance = factory(services!);

            // Record the creating parent once at creation — see the generic overload.
            if (instance is Forms.IFormControl fc)
            {
                Forms.BindingConsumerRegistry.Record(fc, this);
            }
        }

        instance.RenderHandle ??= handle;

        (Live.Children ??= new Dictionary<(Type, int), Component>())[key] = instance;
        return instance;
    }

    private static void ScheduleAsyncContinuation(Component c, Task t, bool rerender)
    {
        if (t.IsCompleted)
        {
            if (t.IsFaulted)
            {
                ReportLifecycleFault(c, t.Exception);
            }

            return;
        }

        t.ContinueWith(static (task, state) =>
        {
            var (comp, doRerender) = ((Component, bool))state!;
            if (task.IsFaulted)
            {
                ReportLifecycleFault(comp, task.Exception);
                return;
            }

            if (task.IsCanceled)
            {
                return;
            }

            if (doRerender)
            {
                comp.StateHasChanged();
            }
        }, (c, rerender), TaskContinuationOptions.ExecuteSynchronously);
    }

    // Marks this component dirty WITHOUT requesting a render, for the window of an async callback.
    //
    // AutoCallback calls it before awaiting a parent-supplied async delegate, which is what lets the
    // component show an intermediate state — a spinner around a fetch — with no StateHasChanged of its own.
    // The mid-await render (Component.InvokeWithRenderingAsync, driven by HandlerSyncContext when the user's
    // task actually yields) walks the tree and serves any clean component from the render cache; without this
    // the owner is still clean at that moment, so its `_loading = true` is invisible and the spinner only
    // "appears" after the fetch it was meant to cover.
    //
    // This mirrors what the DOM-handler path already does verbatim (TryInvokeHandlerAsync: "Set BEFORE
    // running so intermediate renders inside an async handler already see the owner as dirty"). The two paths
    // disagreeing is the bug: an async Button(OnClickAsync:) could paint mid-flight but the identical
    // BsDataGrid(OnSortChangeAsync:) could not.
    //
    // Deliberately not StateHasChanged(): that would also RequestRenderAsync, firing an extra render before
    // the user's code has run. Only the flag is wanted — the render is already coming.
    internal void MarkDirtyForAsyncHandler()
    {
        if (!Live.IsUnmounted)
        {
            Live.StateDirty = true;
        }
    }

    public void StateHasChanged()
    {
        if (Live.IsUnmounted)
        {
            return;
        }

        Live.StateDirty = true;
        var handle = RenderHandle;
        if (handle is null)
        {
            return;
        }

        _ = handle.RequestRenderAsync();
    }

    // Internal-only equivalent of StateHasChanged that flips the dirty flag without
    // scheduling a render. RootErrorBoundary uses this to propagate "force the inner
    // root to re-execute Render() this frame" semantics — the same behavior
    // RenderAsLiveRootCore applies to its own root.
    internal void MarkDirtyForFrame() => Live.StateDirty = true;

    // Test hooks for the Phase B clean-subtree frame cache: whether this component's rendered
    // subtree was cached as frames, and whether it still retains its Element object graph. A cached
    // component has the first true and the second false (the graph was released).
    internal bool IsCleanSubtreeCachedForTest => _live?.Cached is not null;
    internal bool RetainsElementGraphForTest => _live?.CachedRenderResult is not null;

    public Task StateHasChangedAsync()
    {
        if (Live.IsUnmounted)
        {
            return Task.CompletedTask;
        }

        Live.StateDirty = true;
        return RenderHandle?.RequestRenderAsync() ?? Task.CompletedTask;
    }

    internal string RegisterHandler(Delegate handler) =>
        RegisterHandler(handler, this);

    /// <summary>
    ///     Open a new handler-slot generation on this render root, telling every component to restart
    ///     its own slot numbering the next time it registers. Called once per live render pass, from
    ///     <see cref="LiveRenderContext" />'s constructor.
    /// </summary>
    internal void BeginHandlerGeneration() =>
        (Live.HandlerState ??= new HandlerState()).Generation =
            Interlocked.Increment(ref _renderGenerationSource);

    internal string RegisterHandler(Delegate handler, Component owner)
    {
        // Two roles, kept deliberately separate:
        //
        //  * slotComponent — CurrentParent, i.e. the component whose SUBTREE this element is being
        //    serialized in. It anchors the numbering: each component numbers the handlers appearing in
        //    its own subtree 0, 1, 2… in walk order, so nothing outside that subtree can renumber them.
        //    Note "serialized in", not "rendered by": an element built in a parent's Render() and passed
        //    into a wrapper as indexer children serializes inside the WRAPPER's scope and takes a slot
        //    there. So `Card()[cond ? Button(a) : null, Button(b)]` still renumbers Button(b) when the
        //    condition flips — the renumbering is bounded to that one wrapper rather than running to the
        //    end of the page, which is the guarantee, and it is what CleanSubtreeCache relies on.
        //  * dispatchOwner — the component to dirty-mark after the handler runs. For lambdas / method
        //    groups that close over `this` inside a Component subclass (`() => _field++`,
        //    `OnSubmit: SubmitHandler`), DelegateOwner resolves the component that owns the state, so
        //    an element built in ComponentA.Render() but rendered inside ComponentB's subtree (passed
        //    as a child of a composite wrapper) still re-renders A. It also unwraps a closure that
        //    captured `this` alongside a local (`() => _active = index`).
        //
        // Anchoring the SLOT to that resolved target instead would undo the whole point: a callback
        // passed down into a wrapper would consume a slot on the component it was passed FROM, and so
        // shift that component's own ids from outside its own render.
        var slotComponent = owner;
        var dispatchOwner = DelegateOwner.Resolve(handler) is { } target ? target : owner;

        // `this` is always the render root — LiveRenderContext.RegisterHandler dispatches to _root — so
        // the id source, the generation and the handler map all live on one node.
        var rootState = Live.HandlerState ??= new HandlerState();
        var map = Live.Handlers ??= new Dictionary<string, (Component, Delegate)>();

        var slotState = slotComponent.Live.HandlerState ??= new HandlerState();

        // Slot ids are drawn from ONE root's sequence, so a component that arrives under a different
        // root has to re-mint: handing back an id the new root never issued would let the number it
        // does issue next collide, wiring two elements to a single entry in the map. Reachable through
        // ordinary API — rendering the same instance a second time builds a fresh root.
        if (!ReferenceEquals(slotState.MintedUnder, rootState))
        {
            slotState.MintedUnder = rootState;
            slotState.Slot0Id = null;
            slotState.RestIds = null;
            slotState.Stamp = 0;
        }

        // Per-component slot index, reset the first time this component registers in a given root
        // render. The generation stamp (rather than a "reset me" call at scope entry) is what keeps a
        // component that somehow gets walked twice in one frame numbering forward instead of reusing
        // its own slots.
        if (slotState.Stamp != rootState.Generation)
        {
            slotState.Stamp = rootState.Generation;
            slotState.LocalCount = 0;
        }

        var id = SlotId(slotState, rootState, slotState.LocalCount++);
        map[id] = (dispatchOwner, handler);
        return id;
    }

    // The id for one of a component's slots: minted the first time a render reaches that slot, then
    // held for the component's lifetime. Existing slots never renumber, which is the invariant the
    // clean-subtree cache rests on — an unchanged component's baked-in ids are still exactly the ids a
    // walk would issue now, no matter what the rest of the page did.
    //
    // A number is never handed to a SECOND COMPONENT. When a component unmounts, its ids simply stop
    // being re-registered, so a stale in-flight event for a removed element resolves to nothing and
    // safely no-ops; a free-list would instead redirect that click to whichever component took the
    // number. The number space therefore grows with CUMULATIVE rather than concurrent slots — but the
    // id STRING is cached per slot, so a steady-state re-render allocates nothing however large the
    // numbers get, and only a brand-new slot past the interned table costs a one-off string.
    //
    // WITHIN one component a slot is still reused: a component that renders [Cancel, Delete] while
    // editing and [Delete] otherwise gives Delete slot 0 — Cancel's — once editing ends, so an
    // in-flight click on Cancel can land on Delete. That is unchanged from the page-wide counter this
    // replaced (which reassigned far more aggressively), and HandlerFrameShape.Accepts is what narrows
    // it: a frame can only run a handler it can actually feed. Closing it completely would mean keying
    // slots on something stabler than emission order, which is a separate change.
    //
    // Slot 0 is a scalar, so the common single-handler component never allocates an array; slots 1..
    // share one geometrically grown array, allocated only when a second handler appears.
    private static string SlotId(HandlerState slotState, HandlerState rootState, int slot)
    {
        if (slot == 0)
        {
            return slotState.Slot0Id ??= HandlerId(rootState.NextNumber++);
        }

        var index = slot - 1;
        var rest = slotState.RestIds;
        if (rest is null || index >= rest.Length)
        {
            // Double (or jump straight to the needed size) so a component registering many handlers
            // pays O(n) total array allocation, not O(n²). New entries default to null = unassigned.
            var grown = new string[Math.Max(index + 1, Math.Max(2, (rest?.Length ?? 0) * 2))];
            if (rest is { Length: > 0 })
            {
                Array.Copy(rest, grown, rest.Length);
            }

            slotState.RestIds = rest = grown;
        }

        return rest[index] ??= HandlerId(rootState.NextNumber++);
    }

    // The id already issued for a slot, or null when that slot has never been reached. Capture and
    // replay read through this so neither can mint a number as a side effect.
    private static string? IssuedSlotId(HandlerState state, int slot) =>
        slot == 0
            ? state.Slot0Id
            : state.RestIds is { } rest && slot - 1 < rest.Length ? rest[slot - 1] : null;

    private static string[] BuildSmallHandlerIds(int n)
    {
        var arr = new string[n];
        for (var i = 0; i < n; i++)
        {
            arr[i] = "h" + i;
        }

        return arr;
    }

    // Overflow path for renders with > _smallHandlerIds.Length handlers in one root.
    // The prebake covers 1024 handlers per render — orders of magnitude past anything
    // realistic. When a VirtualizeModel / huge keyed list pushes past that, stackalloc + a
    // direct TryFormat skips the int.ToString allocation that `"h" + n` would force.
    private static string CreateLargeHandlerId(int n)
    {
        Span<char> buf = stackalloc char[12];
        buf[0] = 'h';
        return n.TryFormat(buf[1..], out var written, provider: CultureInfo.InvariantCulture)
            ? new string(buf[..(1 + written)])
            : "h" + n.ToString(CultureInfo.InvariantCulture);
    }

    // ---- Clean-subtree handler round-trip (see CachedSubtree.Handlers) -----------------------------
    //
    // These exist so a replayed subtree can reproduce the walk's effect on handler state: the root's
    // map is cleared every render, so a replay that re-emitted a handler's id without re-registering it
    // would leave a silently dead button. Called on the SLOT component (the one being cached), with the
    // root passed in because that is where the map lives.

    /// <summary>
    ///     The (owner, delegate) pairs this component registered during the walk that just finished, in
    ///     slot order, or <c>null</c> when it registered none. A cacheable subtree contains no nested
    ///     user component, so every handler inside it was registered under THIS component's slots —
    ///     which is what lets the run be described by a slot count rather than by a position in some
    ///     page-wide sequence that anything upstream could move.
    /// </summary>
    internal (Component Owner, Delegate Handler)[]? CaptureHandlerRun(Component root)
    {
        var state = _live?.HandlerState;
        var rootState = root._live?.HandlerState;

        // A stale stamp means this component registered nothing this render, so LocalCount still
        // belongs to an older generation and must not be read as a count.
        if (state is null
            || rootState is null
            || state.Stamp != rootState.Generation
            || state.LocalCount <= 0
            || root.Live.Handlers is not { } map)
        {
            return null;
        }

        var run = new (Component, Delegate)[state.LocalCount];
        for (var i = 0; i < run.Length; i++)
        {
            if (IssuedSlotId(state, i) is not { } id || !map.TryGetValue(id, out var entry))
            {
                // The run isn't what we assumed it was; caching it would risk a dead handler on replay.
                return null;
            }

            run[i] = entry;
        }

        return run;
    }

    /// <summary>
    ///     Re-register a captured run under this component's own slot ids, leaving the root's handler
    ///     map exactly as the skipped walk would have. The ids belong to the component rather than to a
    ///     page-wide sequence, so there is nothing upstream that could have invalidated them.
    /// </summary>
    internal void ReplayHandlerRun(Component root, (Component Owner, Delegate Handler)[] run)
    {
        var state = _live!.HandlerState!;
        var map = root.Live.Handlers ??= new Dictionary<string, (Component, Delegate)>();
        for (var i = 0; i < run.Length; i++)
        {
            // Non-null by construction: CaptureHandlerRun refuses a run whose slots weren't all issued,
            // and a slot id is never surrendered once minted.
            map[IssuedSlotId(state, i)!] = run[i];
        }
    }

    // The id for slot number n. Shared by issue / capture / replay so all three agree by construction.
    // Unsigned compare so a wrapped (negative) number falls to the format path and yields "h-1" rather
    // than indexing the table out of bounds. Numbers now accumulate over a session's whole life instead
    // of resetting each render, so overflow is reachable in principle — 2^31 slots — where it was not.
    private static string HandlerId(int n) =>
        (uint)n < (uint)_smallHandlerIds.Length ? _smallHandlerIds[n] : CreateLargeHandlerId(n);

    internal ValueTask<bool> TryInvokeHandlerAsync(string id, JsonElement payload)
        => TryInvokeHandlerAsync(id, payload, null);

    internal async ValueTask<bool> TryInvokeHandlerAsync(
        string id, JsonElement payload, IServiceProvider? services, CancellationToken dispatchToken = default)
    {
        if (Live.Handlers is null || !Live.Handlers.TryGetValue(id, out var entry))
        {
            return false;
        }

        var (owner, handler) = entry;

        // The id said WHICH handler; the frame's own `type` says what it is carrying. An id names a
        // component's slot for that component's lifetime, so a frame that outlived its render usually
        // resolves to the same handler or to nothing at all — but a component that swapped what its
        // slot 0 renders (an input handler this frame, a click handler the next) still lands a stale
        // `input` message on a parameterless callback. A frame that cannot feed the handler it landed
        // on is a stale id by definition, and is answered exactly like one.
        if (!HandlerFrameShape.Accepts(payload, handler))
        {
            return false;
        }

        using var __dispatchScope = DispatchServicesScope.Push(services);

        // A handler runs outside any render walk, so there is no LiveRenderContext to read the culture
        // from — push it here, from the same service provider, so code the handler calls formats and
        // parses in the visitor's culture rather than the machine's.
        using var __cultureScope = Globalization.RaskCultureScope.PushFrom(services);

        // When the host supplied a cancellable dispatch token (a handler timeout is configured), make
        // owner.CancellationToken observe it during this handler by publishing a token linked with the
        // owner's lifetime token. With no timeout we push nothing — CancellationToken then resolves to
        // the plain lifetime token — so the common path stays allocation-free.
        CancellationTokenSource? linkedCts = null;
        IDisposable? eventTokenScope = null;
        if (dispatchToken.CanBeCanceled)
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(owner.LifetimeToken, dispatchToken);
            eventTokenScope = DispatchEventTokenScope.Push(linkedCts.Token);
        }

        using var __linked = linkedCts;
        using var __eventTokenScope = eventTokenScope;

        // Match Blazor: every event handler implicitly marks the registering component
        // dirty. Set BEFORE running so intermediate renders inside an async handler
        // (via InvokeWithRenderingAsync) already see the owner as dirty.
        owner.Live.StateDirty = true;
        try
        {
            switch (handler)
            {
                case Action a:
                    a();
                    return true;
                case Action<MouseModifiers> am:
                    am(ExtractModifiers(payload));
                    return true;
                case Func<Task> f:
                    await InvokeWithRenderingAsync(f).ConfigureAwait(false);
                    // The mid-await render inside InvokeWithRenderingAsync resets Live.StateDirty
                    // to false when it walks the owner's subtree. Re-mark dirty here so the
                    // dispatcher's post-handler render picks up state mutated AFTER the
                    // mid-await window (e.g. an async validator's terminal message, or a
                    // user lambda that ran on the continuation of an awaited Task).
                    owner.Live.StateDirty = true;
                    return true;
                case Func<MouseModifiers, Task> fm:
                    var modsForAsync = ExtractModifiers(payload);
                    await InvokeWithRenderingAsync(() => fm(modsForAsync)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                case Action<string> a:
                    a(ExtractString(payload, "value"));
                    return true;
                case Func<string, Task> f:
                    var s = ExtractString(payload, "value");
                    await InvokeWithRenderingAsync(() => f(s)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                case Action<IReadOnlyList<string>> a:
                    a(ExtractStringList(payload));
                    return true;
                case Func<IReadOnlyList<string>, Task> f:
                    var values = ExtractStringList(payload);
                    await InvokeWithRenderingAsync(() => f(values)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                case Action<FormData> a:
                    a(FormData.FromJson(payload));
                    return true;
                case Func<FormData, Task> f:
                    var data = FormData.FromJson(payload);
                    await InvokeWithRenderingAsync(() => f(data)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                case Action<ScrollEvent> a:
                    a(ScrollEvent.FromJson(payload));
                    return true;
                case Func<ScrollEvent, Task> f:
                    var scroll = ScrollEvent.FromJson(payload);
                    await InvokeWithRenderingAsync(() => f(scroll)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                case Action<KeyboardEventArgs> a:
                    a(KeyboardEventArgs.FromJson(payload));
                    return true;
                case Func<KeyboardEventArgs, Task> f:
                    var key = KeyboardEventArgs.FromJson(payload);
                    await InvokeWithRenderingAsync(() => f(key)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                case Action<IReadOnlyList<RaskFile>> a:
                {
                    var files = FileListReader.Read(payload);
                    try { a(files); }
                    finally { ReleaseFiles(files); }

                    return true;
                }
                case Func<IReadOnlyList<RaskFile>, Task> f:
                {
                    var files = FileListReader.Read(payload);
                    try
                    {
                        await InvokeWithRenderingAsync(() => f(files)).ConfigureAwait(false);
                    }
                    finally { ReleaseFiles(files); }

                    owner.Live.StateDirty = true;
                    return true;
                }
                // Extended GlobalEventHandlers args (mouse/wheel/pointer/touch/clipboard/media). Each
                // parses the flat client payload into its typed record; async siblings re-mark dirty
                // after the mid-await render, mirroring the keyboard/scroll cases above.
                case Action<MouseEventArgs> c:
                    c(MouseEventArgs.FromJson(payload));
                    return true;
                case Func<MouseEventArgs, Task> c:
                {
                    var args = MouseEventArgs.FromJson(payload);
                    await InvokeWithRenderingAsync(() => c(args)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                }
                case Action<WheelEventArgs> c:
                    c(WheelEventArgs.FromJson(payload));
                    return true;
                case Func<WheelEventArgs, Task> c:
                {
                    var args = WheelEventArgs.FromJson(payload);
                    await InvokeWithRenderingAsync(() => c(args)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                }
                case Action<PointerEventArgs> c:
                    c(PointerEventArgs.FromJson(payload));
                    return true;
                case Func<PointerEventArgs, Task> c:
                {
                    var args = PointerEventArgs.FromJson(payload);
                    await InvokeWithRenderingAsync(() => c(args)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                }
                case Action<TouchEventArgs> c:
                    c(TouchEventArgs.FromJson(payload));
                    return true;
                case Func<TouchEventArgs, Task> c:
                {
                    var args = TouchEventArgs.FromJson(payload);
                    await InvokeWithRenderingAsync(() => c(args)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                }
                case Action<ClipboardEventArgs> c:
                    c(ClipboardEventArgs.FromJson(payload));
                    return true;
                case Func<ClipboardEventArgs, Task> c:
                {
                    var args = ClipboardEventArgs.FromJson(payload);
                    await InvokeWithRenderingAsync(() => c(args)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                }
                case Action<MediaEventArgs> c:
                    c(MediaEventArgs.FromJson(payload));
                    return true;
                case Func<MediaEventArgs, Task> c:
                {
                    var args = MediaEventArgs.FromJson(payload);
                    await InvokeWithRenderingAsync(() => c(args)).ConfigureAwait(false);
                    owner.Live.StateDirty = true;
                    return true;
                }
                default:
                {
                    // Parameterless delegate shapes outside the fast-path list above can still arrive
                    // through a typed handler slot (e.g. a method group typed Func<Task<T>> or
                    // Func<ValueTask> wired to a drag handler). Invoke reflectively;
                    // if the result is an awaitable, pump it through the render path so exceptions reach
                    // the ErrorBoundary and post-await state changes re-render — matching the explicit
                    // Func<…, Task> cases. Without this, a returned Task is fire-and-forget: a fault is
                    // unobserved and post-await mutations never render.
                    var result = handler.DynamicInvoke();
                    var pending = result switch
                    {
                        Task t => t,
                        ValueTask vt => vt.AsTask(),
                        _ => null
                    };
                    if (pending is not null)
                    {
                        await InvokeWithRenderingAsync(() => pending).ConfigureAwait(false);
                        owner.Live.StateDirty = true;
                    }

                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ResolveHandlerBoundary(owner) is not null)
        {
            // Route handler exceptions to the boundary that logically contains the handler.
            // When the owner is itself an ErrorBoundary (the common case: a button rendered
            // directly inside ErrorBoundary's Children — CurrentParent at registration time
            // is the boundary), THAT boundary catches. owner.Boundary would route one level
            // higher. For non-boundary owners (regular components), fall back to their
            // ancestor boundary. Without a boundary the exception bubbles so the dispatcher's
            // catch-and-log still fires.
            ResolveHandlerBoundary(owner)!.Trip(ex, ErrorSource.Action);
            return true;
        }
    }

    private async Task InvokeWithRenderingAsync(Func<Task> invoke)
    {
        var handle = RenderHandle;
        if (handle is null)
        {
            await invoke().ConfigureAwait(false);
            return;
        }

        var prev = SynchronizationContext.Current;
        var ctx = new HandlerSyncContext(handle.RenderInScopeAsync);
        SynchronizationContext.SetSynchronizationContext(ctx);
        try
        {
            var userTask = invoke();
            if (!userTask.IsCompleted)
            {
                // Suspend HandlerSyncContext for the duration of the render-and-send. Kestrel's
                // WebSocket.SendAsync has internal awaits that don't all use ConfigureAwait(false),
                // so any leaking ambient sync context becomes the target for its flush
                // continuation. With HandlerSyncContext as the target, that continuation Posts a
                // RunWithRendersAsync, which fires *another* render-and-send on the same socket —
                // a recursive render chain that races the in-flight one, the WS lock, and the
                // user's still-pending async work. Restoring prev for the render call confines
                // HandlerSyncContext to the user-code window where it's actually meaningful.
                SynchronizationContext.SetSynchronizationContext(prev);
                try
                {
                    await handle.RenderInScopeAsync().ConfigureAwait(false);
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(ctx);
                }
            }

            await userTask.ConfigureAwait(false);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prev);
            await ctx.DrainAsync().ConfigureAwait(false);
        }
    }

    internal string RenderAsLiveRoot() => RenderAsLiveRootCore(null, false, sink: null)!;

    internal string RenderAsLiveRoot(IServiceProvider services) => RenderAsLiveRootCore(services, false, sink: null)!;

    internal string RenderAsLiveRoot(IServiceProvider services, bool publishOnly) =>
        RenderAsLiveRootCore(services, publishOnly, sink: null)!;

    /// <summary>
    ///     Live-update variant of <see cref="RenderAsLiveRoot(IServiceProvider, bool)" /> that renders the
    ///     page into <paramref name="sink" />'s reused char buffer instead of a fresh string, so a live
    ///     session's diff path allocates nothing for the page HTML. The session reads the rendered chars
    ///     back via <see cref="RenderedHtmlBuffers.Current" />.
    /// </summary>
    internal void RenderAsLiveRootInto(IServiceProvider services, bool publishOnly, RenderedHtmlBuffers sink) =>
        RenderAsLiveRootCore(services, publishOnly, sink);

    // Returns the rendered page as a string when sink is null; when a sink is supplied the page is
    // copied into it instead and null is returned (the caller reads sink.Current).
    private string? RenderAsLiveRootCore(IServiceProvider? services, bool publishOnly, RenderedHtmlBuffers? sink)
    {
        // Reuse the handler map across renders, but clear it: a component that left the tree must not
        // keep a live registration, and every component still in it re-registers during the walk (or,
        // for a replayed subtree, via ReplayHandlerRun). Lazy-init only on the very first render of
        // this component as a root.
        //
        // The IDS are not reset. They belong to (component, slot) and stick for the component's
        // lifetime, so a component that renders unchanged re-registers under exactly the ids already
        // baked into the page — which is what lets the diff leave its data-rask-on-* attributes alone
        // and lets its cached subtree replay. What restarts each component's own slot numbering is the
        // generation, stamped by LiveRenderContext's constructor a few lines below.
        Live.Handlers ??= new Dictionary<string, (Component, Delegate)>();
        Live.Handlers.Clear();
        // Lazily init on first root render — non-root Component instances (the 99% case for
        // leaf Elements in a page) never touch this field and stay allocation-free.
        var previousEditContexts =
            Live.PersistedEditContexts ??= new Dictionary<LiveRenderContext.ObjectKey, EditContext>();
        // Recycle the previously-snapshotted dict as the next frame's `current`. First
        // render: pool is null, allocate once. Steady state: Clear and reuse.
        Live.EditContextsPool ??= new Dictionary<LiveRenderContext.ObjectKey, EditContext>();
        Live.EditContextsPool.Clear();
        // Reuse the head-asset collector and mounted-type set across renders (cleared here),
        // so head emission doesn't allocate fresh lists/sets every frame.
        Live.HeadAssets ??= new HeadAssetRegistry();
        Live.HeadAssets.Clear();
        Live.MountedTypes ??= new HashSet<Type>();
        Live.MountedTypes.Clear();
        using var ctx = LiveRenderContext.Begin(
            this, previousEditContexts, Live.EditContextsPool, services, Live.HeadAssets, Live.MountedTypes);

        // Pooled per-frame scratch buffers held on the root component. RenderAsLiveRootCore
        // runs single-threaded per session (the WS dispatcher serializes via the session
        // lock), so reusing these in place is safe and saves three allocations per render
        // after warmup.
        Live.AlivePrev ??= new HashSet<Component>(ReferenceEqualityComparer.Instance);
        Live.AliveNow ??= new HashSet<Component>(ReferenceEqualityComparer.Instance);
        Live.ParentMap ??= new Dictionary<Component, Component>(ReferenceEqualityComparer.Instance);
        Live.AlivePrev.Clear();
        Live.AliveNow.Clear();
        Live.ParentMap.Clear();

        // Snapshot the alive set AND parent map BEFORE we touch _children. Walking via
        // every component's _children gives us the same view the previous successful render
        // produced. The parent map is needed in the dispose pass to suppress double-dispose
        // of descendants in a torn-down subtree.
        CollectAliveWithParents(this, Live.AlivePrev, Live.ParentMap);

        // RenderAsLiveRoot is the explicit "render now" entry point — called for the initial
        // GET, WS reconnect recovery render, hot reload, and from tests. Force the root to
        // re-execute Render() this frame; descendants still skip on their own diff. Without
        // this, a second RenderAsLiveRoot call with no descendant marked dirty would skip the
        // root, never re-binding closure-captured state or reading external mutable state.
        Live.StateDirty = true;
        RaiseLifecycleBeforeRender(false);

        // Serialize straight into a pooled builder and splice the head-asset block in place,
        // so the page materializes to a string exactly once (the final ToString). The previous
        // path allocated the page TWICE — ToHtml() produced one full-page string, then ApplyTo
        // copied the whole page into a second builder to inject the head assets.
        var pageBuilder = RaskStringBuilderPool.Shared.Get();
        string? html = null;
        try
        {
            HtmlSerializer.Serialize(this, pageBuilder);

            // Splice component-declared <head> contributions into the RaskHeadAssets sentinel.
            // The registry was populated by HtmlSerializer as it descended through user
            // components; we resolve the active context (still live before the using-disposal
            // below) and apply once. The sentinel offset was recorded during serialization
            // (HeadSentinelIndex), so no whole-page IndexOf scan is needed here.
            if (LiveRenderContext.Current is { } liveCtx)
            {
                // ApplyInPlace replaces the head-asset sentinel in place, shifting every byte
                // position after it. The diff codec's frame offsets were captured against the
                // pre-splice HTML, so when a frame stream is being captured (diff path) we must
                // move the offsets past the sentinel by the same delta — otherwise an
                // InsertSubtree fragment (sliced from this post-splice HTML via those offsets)
                // reads the wrong bytes.
                var sentinelIdx = liveCtx.HeadSentinelIndex;
                var preLen = pageBuilder.Length;
                liveCtx.HeadAssets.ApplyInPlace(pageBuilder, sentinelIdx, liveCtx.Services);
                if (sentinelIdx >= 0 && FrameSinkScope.Current is { } frameSink)
                {
                    frameSink.AdjustOffsetsFrom(
                        sentinelIdx + HeadAssetRegistry.Sentinel.Length,
                        pageBuilder.Length - preLen);
                }
            }

            // Materialise the page exactly once: into the session's reused char buffer on the live-update
            // path (zero GC), or into a fresh string for the first-render / test / full-HTML-fallback path.
            if (sink is not null)
            {
                sink.CopyFrom(pageBuilder);
            }
            else
            {
                html = pageBuilder.ToString();
            }
        }
        finally
        {
            RaskStringBuilderPool.Shared.Return(pageBuilder);
        }

        // Post-render alive set: union of _children across the whole tree, reachable from root.
        // Components that re-rendered have fresh _children; components that skipped kept theirs.
        CollectAlive(this, Live.AliveNow);

        foreach (var child in Live.AliveNow)
        {
            if (!ReferenceEquals(child, this))
            {
                child.RaiseOnRendered(publishOnly);
            }
        }

        RaiseOnRendered(publishOnly);

        // DisposeComponentTree recurses through PersistedChildren — so disposing a parent
        // ALSO disposes its descendants. To avoid disposing each descendant twice, only
        // dispose components whose previously-alive parent is still alive (or whose parent
        // is the root); the parent's recursion will handle the rest.
        foreach (var prev in Live.AlivePrev)
        {
            if (Live.AliveNow.Contains(prev) || ReferenceEquals(prev, this))
            {
                continue;
            }

            // If our previous parent is also being disposed in this pass, the parent's
            // DisposeComponentTree will cover us — skip to avoid double-dispose.
            if (Live.ParentMap.TryGetValue(prev, out var parent) &&
                !Live.AliveNow.Contains(parent) &&
                !ReferenceEquals(parent, this))
            {
                continue;
            }

            ComponentLifecycle.DisposeComponentTree(prev);
        }

        // Swap: the dict we wrote into this frame becomes next frame's `previous`;
        // the now-stale previous becomes the pool that next frame will Clear and reuse.
        var snapshot = ctx.SnapshotEditContexts();
        // Dispose EditContexts that were alive last frame but weren't re-resolved this frame —
        // i.e. the form they back was unmounted. Their sticky-dismissal timers would otherwise
        // fire once more after teardown (pinning the context + render handle for the sticky
        // tail). Compare by instance, not key: a Form shares one EditContext across its root
        // model plus every sub-model key, so a context is dead only when no surviving key still
        // points at it. Guarded on Count so the common form-free page pays nothing.
        DisposeUnmountedEditContexts(previousEditContexts, snapshot);
        Live.EditContextsPool = Live.PersistedEditContexts;
        Live.PersistedEditContexts = snapshot;
        return html;
    }

    // Disposes EditContexts present in `previous` (last frame's set) whose instance no longer
    // appears in `current` (this frame's set) — the forms they back were unmounted. Compares by
    // reference identity because one EditContext is shared across a Form's many model keys, so a
    // context survives if ANY current key still references it. No-ops when there's nothing to do.
    private static void DisposeUnmountedEditContexts(
        Dictionary<LiveRenderContext.ObjectKey, EditContext> previous,
        Dictionary<LiveRenderContext.ObjectKey, EditContext> current)
    {
        if (previous.Count == 0)
        {
            return;
        }

        var survivors = new HashSet<EditContext>(ReferenceEqualityComparer.Instance);
        foreach (var ctx in current.Values)
        {
            survivors.Add(ctx);
        }

        foreach (var ctx in previous.Values)
        {
            if (!survivors.Contains(ctx))
            {
                ctx.Dispose();
            }
        }
    }

    private static void CollectAlive(Component root, HashSet<Component> seen)
    {
        Visit(root, seen);

        static void Visit(Component c, HashSet<Component> seen)
        {
            if (!seen.Add(c))
            {
                return;
            }

            if (c._live?.Children is null)
            {
                return;
            }

            foreach (var child in c._live.Children.Values)
            {
                Visit(child, seen);
            }
        }
    }

    // Dev-only (C# Hot Reload): mark every live, mounted component in the tree StateDirty so the next
    // render re-executes each Render() — including cached subtrees — against the freshly-applied IL. A
    // component with no LiveState has never rendered (no cache to bust), so it's skipped. Called from
    // LiveSessionBase.RerenderAllForHotReload under `dotnet watch`; best-effort (the caller swallows).
    internal static void MarkSubtreeDirtyInternal(Component root)
    {
        var seen = new HashSet<Component>();
        Visit(root, seen);

        static void Visit(Component c, HashSet<Component> seen)
        {
            if (!seen.Add(c))
            {
                return;
            }

            if (c._live is { IsUnmounted: false } live)
            {
                live.StateDirty = true;
            }

            if (c._live?.Children is null)
            {
                return;
            }

            foreach (var child in c._live.Children.Values)
            {
                Visit(child, seen);
            }
        }
    }

    private static void CollectAliveWithParents(
        Component root,
        HashSet<Component> seen,
        Dictionary<Component, Component> parents)
    {
        Visit(root, seen, parents);

        static void Visit(Component c, HashSet<Component> seen, Dictionary<Component, Component> parents)
        {
            if (!seen.Add(c))
            {
                return;
            }

            if (c._live?.Children is null)
            {
                return;
            }

            foreach (var child in c._live.Children.Values)
            {
                parents[child] = c;
                Visit(child, seen, parents);
            }
        }
    }

    private static string ExtractString(JsonElement payload, string property)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(property, out var v))
        {
            return string.Empty;
        }

        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>
    ///     The whole selection a <c>&lt;select multiple&gt;</c> reported, from the frame's <c>values</c>
    ///     array. Falls back to the single <c>value</c> so the handler still sees the user's pick when
    ///     the array is absent — a single-value control wired to a list handler, or a browser holding a
    ///     cached client from a deploy that predates the array.
    /// </summary>
    private static IReadOnlyList<string> ExtractStringList(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("values", out var v)
            && v.ValueKind == JsonValueKind.Array)
        {
            var length = v.GetArrayLength();
            if (length == 0)
            {
                return [];
            }

            var picked = new string[length];
            var i = 0;
            foreach (var item in v.EnumerateArray())
            {
                picked[i++] = item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? string.Empty
                    : string.Empty;
            }

            return picked;
        }

        var single = ExtractString(payload, "value");
        // "" is what an empty select reports, and it is not a selection — reporting it as one option
        // named "" would make "nothing picked" indistinguishable from "picked the blank option".
        return single.Length == 0 ? [] : new[] { single };
    }

    private static MouseModifiers ExtractModifiers(JsonElement payload) =>
        new(ExtractBool(payload, "shiftKey"),
            ExtractBool(payload, "ctrlKey"),
            ExtractBool(payload, "altKey"),
            ExtractBool(payload, "metaKey"));

    private static bool ExtractBool(JsonElement payload, string property)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(property, out var v))
        {
            return false;
        }

        return v.ValueKind == JsonValueKind.True;
    }

    private static void ReleaseFiles(IReadOnlyList<RaskFile> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        FileListReader.ResolveBackend()?.Release(files);
    }

    /// <summary>
    ///     Everything a clean-subtree snapshot needs, hung off <see cref="LiveState.Cached" /> so only
    ///     the components that actually cache pay for it — see the note on that field.
    ///     <para>
    ///         Held instead of the Element object graph: on capture the subtree's frames are leaned down
    ///         into <see cref="Frames" /> and <c>CachedRenderResult</c> is released, so a clean re-render
    ///         replays from here and never touches an element again. Everything else on this object exists
    ///         to prove the snapshot is still valid to replay — identity and handler wiring that a walk
    ///         would have re-established and a replay must reproduce exactly.
    ///     </para>
    /// </summary>
    private sealed class CachedSubtree
    {
        /// <summary>
        ///     The leaned-down frame span. <c>Frames.Length</c> may exceed <see cref="FrameCount" /> — the
        ///     array is reused across captures so a component that re-renders every frame re-captures with
        ///     zero allocation. <c>LeanFrame</c> (~24 B) rather than <c>RenderFrame</c> (~40 B): a held
        ///     snapshot never needs the per-render HTML offsets or the diff-only component ref, both of
        ///     which replay regenerates.
        /// </summary>
        public LeanFrame[] Frames = [];

        public int FrameCount;

        /// <summary>
        ///     The <c>data-rask-key</c> identity baked into <see cref="Frames" />: the component's own
        ///     Key, or — when it has none — the ancestor-forwarded key its first element adopted. One
        ///     slot, because only one can ever reach our elements (an own Key overwrites the forwarded
        ///     one). Either can change without dirtying the component, so the replay re-checks it.
        /// </summary>
        public object? KeyIdentity;

        /// <summary>
        ///     The subtree's event handlers in the order the walk registered them, or <c>null</c> when it
        ///     has none. Ids are NOT stored: a cached subtree has no nested user component, so entry
        ///     <c>i</c> is this component's own slot <c>i</c> and its id is read back off the slot table
        ///     — one less reference per entry, and the id is settled for the component's lifetime so
        ///     there is nothing to go stale. Retaining the delegates is not new retention: the released
        ///     Element graph held these very instances.
        /// </summary>
        public (Component Owner, Delegate Handler)[]? Handlers;
    }

    /// <summary>
    ///     Handler-id bookkeeping, hung off <see cref="LiveState.HandlerState" /> so a component that
    ///     renders no handler pays one null reference rather than a field per role. The render root
    ///     uses the first two members (it owns the id source); a component that renders handlers uses
    ///     the rest (its own slot table). See <see cref="RegisterHandler(Delegate, Component)" />.
    ///     <para>
    ///         A slot id means something only within the sequence that minted it, so a component that
    ///         turns up under a different root drops its table and re-mints — see
    ///         <see cref="MintedUnder" />. Its ids are not stable across that move, which is correct:
    ///         they were never ids the new root had issued.
    ///     </para>
    /// </summary>
    private sealed class HandlerState
    {
        // ---- render root ----

        /// <summary>
        ///     The next number this root will hand out. Monotonic for the root's whole life: numbers are
        ///     never reset and never reused, so a stale in-flight event cannot be redirected onto a
        ///     component that took a freed number.
        /// </summary>
        public int NextNumber;

        /// <summary>
        ///     Identifies the current root render, so each component resets its slot counter exactly
        ///     once per frame. Drawn from a process-wide source rather than counted per root, because a
        ///     component reached from two different roots must never mistake one root's generation for
        ///     the other's and skip its reset.
        /// </summary>
        public long Generation;

        // ---- a component that renders handlers ----

        /// <summary>
        ///     The <see cref="HandlerState" /> of the root whose sequence minted this component's slot
        ///     ids. Compared by reference on every registration: a component that turns up under a
        ///     different root re-mints, because ids from one root's sequence mean nothing in another's.
        /// </summary>
        public HandlerState? MintedUnder;

        /// <summary>The generation in which <see cref="LocalCount" /> was last reset.</summary>
        public long Stamp;

        /// <summary>Slots this component has used so far in the generation named by <see cref="Stamp" />.</summary>
        public int LocalCount;

        /// <summary>
        ///     The id for slot 0 — a scalar, so the common single-handler component never allocates an
        ///     array. Minted on first use and then held for the component's lifetime.
        /// </summary>
        public string? Slot0Id;

        /// <summary>
        ///     Ids for slots 1.. (index <c>i</c> → slot <c>i+1</c>), grown geometrically and allocated
        ///     only when a second handler appears. A null entry is a slot never yet reached.
        /// </summary>
        public string[]? RestIds;
    }

    // The hoisted state — class so it stays out-of-band from each Component instance.
    // Field grouping mirrors the prior layout for readability of the diff.
    private sealed class LiveState
    {
        public HashSet<Component>? AliveNow;
        public HashSet<Component>? AlivePrev;
        // Hoisted off the base Component: only ever set on live-render roots and user components
        // (which allocate a LiveState anyway), so plain Elements shed these three refs entirely.
        public ErrorBoundary? Boundary;
        public IRenderHandle? RenderHandle;
        public CancellationTokenSource? LifetimeCts;
        public Component? CachedRenderResult;

        // The Head override's output, produced by the same render as CachedRenderResult and re-read
        // (never re-run) by every later collection of it — see Component.CachedHeadInternal. Null on
        // the components that have no Head, which is nearly all of them.
        public Component? CachedHead;

        // Phase B clean-subtree frame cache — see CachedSubtree. ONE reference, not the handful of
        // fields the snapshot actually needs: LiveState is allocated per node on a mounted page, so
        // every field here costs ~8 B on every node of every live session (measured: ~56 KB per field
        // on a 1,000-row page). Hanging the state off a side object moves that cost onto the
        // components that actually cache, and leaves the rest paying a single null reference.
        public CachedSubtree? Cached;
        public int ChildPositions;
        public Dictionary<(Type, int), Component>? Children;
        public Dictionary<LiveRenderContext.ObjectKey, EditContext>? EditContextsPool;
        public ElementRef? ElementRef;
        public string? Role;
        public int? TabIndex;
        public IReadOnlyDictionary<string, string?>? Aria;

        // Every remaining global HTML attribute, in one bag rather than a typed field each. A field here
        // costs ~8 B on every node of every live session (~56 KB per field on a 1,000-row page — see
        // CachedSubtree above), and the shared Element surface has a hard pending-bit budget for
        // folding properties, so one dictionary keeps both budgets where a typed prop each would keep
        // neither. This is the verbatim escape hatch behind Element.Attributes.
        public IReadOnlyDictionary<string, string?>? ExtraAttrs;

        // The typed global attributes that are neither plain-and-always-there (id/class/style/title)
        // nor cheap enough for a flag bit — see Component.GlobalAttrs. ONE reference for the six of
        // them, for exactly the reason the field above gives: six fields here would be ~336 KB on that
        // same 1,000-row page, where this is ~56 KB and only the elements that actually name one of
        // them allocate the side object. Hidden/Inert are in neither place — they are two bits each of
        // the flags byte, because `hidden` is common enough that allocating for it would be a
        // regression (see Element.FlagHiddenPresent).
        public GlobalAttrs? Globals;
        public HeadAssetRegistry? HeadAssets;
        public HashSet<Type>? MountedTypes;
        public Dictionary<string, (Component Owner, Delegate Action)>? Handlers;
        public bool HasInitialized;
        public bool HasRenderedOnce;
        public bool IsDisposed;
        public bool IsUnmounted;
        public HandlerState? HandlerState;
        public Dictionary<Component, Component>? ParentMap;
        public Dictionary<LiveRenderContext.ObjectKey, EditContext>? PersistedEditContexts;
        public Dictionary<(Type, int), Component>? PreviousChildren;
        public bool PropsDirty;
        public bool StateDirty;

        // Keyed child identity (#685). Three more null references on a parent that never keys a child,
        // and the maps are allocated only by one that does — a keyed list is a small fraction of the
        // nodes on a page, so this does not pay the per-node cost the note on Cached describes.
        //
        // KeyedTypes is deliberately CUMULATIVE rather than per-frame: once a parent has identified a
        // child type by key, that type stops being identified by ordinal for the rest of the parent's
        // life. It has to. A key that is new this frame must get a FRESH instance, and the ordinal path
        // would hand it a recycled one belonging to whichever item used to sit at that position — which
        // is the very bug being fixed, moved one step along.
        public HashSet<Type>? KeyedTypes;
        public Dictionary<(Type, object), Component>? KeyedChildren;
        public Dictionary<(Type, object), Component>? PreviousKeyedChildren;

        // The slot the last entry filed a child under, so the Key step that follows it can re-file onto
        // whichever instance the key actually claims. Safe to keep as one field because Key is required
        // to be the FIRST step of its chain (RASK046) — nothing can be built in between.
        public (Type Type, int Ordinal) LastChildSlot;

        // Builder surface. Two more bools rather than a wider record: LiveState is allocated per node
        // on a mounted page, and these two land in the padding the six above already leave behind — so
        // the deferred-commit machinery costs nothing per node (see the note on Cached).
        //
        // EntryPropsChanged: a folding setter wrote a different value since the last commit (the
        // setter-chain equivalent of the factory's __propsChanged). HasEntryChildren: at least one
        // child of THIS component came from a builder entry during the Render() now in flight, so the
        // post-Render commit loop has work to do.
        public bool EntryPropsChanged;
        public bool HasEntryChildren;
    }
}

public readonly record struct MouseModifiers(bool Shift, bool Ctrl, bool Alt, bool Meta);
