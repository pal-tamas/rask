using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.HeadAssets;
using Rask.Core.Live;

namespace Rask.Core;

// [CollectionBuilder] makes `Component` itself a collection-expression target, so a render body
// can be written as `Render() => [Nav, Main[Router]]` (the items are built into a Fragment by
// RaskFragment below). The builder is self-referential (typeof(Component)) and public so collection
// expressions in *other* assemblies bind to it even though Fragment itself is internal. The
// required iteration type comes from the *pattern* GetEnumerator below — Component deliberately
// does NOT implement IEnumerable<Component>, because that would make the `this[IEnumerable<Component>]`
// children indexer applicable to a bare component and silently rebind `Div()[Span()[...]]` from
// "one child" to "the span's own children", collapsing nesting.
[CollectionBuilder(typeof(Component), "RaskFragment")]
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

    // Lifecycle bits every node can set, kept off LiveState: a plain tag reaches both — a chain step on it
    // marks its props changed, and the after-render pass visits it — and allocating a ~260 B LiveState to
    // hold one bool was most of what a mounted row of plain tags retained. Sits in padding beside _flags.
    //   bit 0 — a folding chain setter changed a value since the last commit (CommitEntry reads and clears)
    //   bit 1 — the after-render hooks have fired once (RaiseOnRendered)
    private byte _lifecycleFlags;

    private const byte FlagEntryPropsChanged = 1 << 0;
    private const byte FlagHasRenderedOnce = 1 << 1;

    private bool GetLifecycleFlag(byte mask) => (_lifecycleFlags & mask) != 0;

    private void SetLifecycleFlag(byte mask, bool value) =>
        _lifecycleFlags = value ? (byte)(_lifecycleFlags | mask) : (byte)(_lifecycleFlags & ~mask);

    // Which tag an element renders, as an index into the generated RaskTags table: a type several tags
    // share (h1–h6) is told apart per instance. Packed beside _flags, it sits in padding the object
    // already has. Zero for everything that is not a [Tag] element.
    internal ushort TagId { get; set; }

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

    // Test seam: whether this node has paid for a LiveState.
    internal bool HasLiveStateInternal => _live is not null;

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
    ///     <c>.Key(order.Id)</c> — never the loop index, which changes the moment anything is inserted or
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

    // Null TagName means "not an HTML element" (Fragment/Doctype/Text/Raw/ErrorBoundary/user
    // components). When non-null, HtmlSerializer wraps WriteAttributes(sb)/RenderChildren()
    // output in `<tag>…</tag>` (or self-closes when SelfClosing is true).
    protected virtual string? TagName => null;
    protected virtual bool SelfClosing => false;

    // True when everything below this component's element is owned by something that is not Rask —
    // a React root, a Lit element, a Blazor renderer (see Rask.External). The live diff must then
    // treat the subtree as a single opaque node: it is reconciled by its own framework, on its own
    // schedule, and patching into it means two writers on one subtree. That failure does not throw;
    // it corrupts on the next parent re-render, which is why the marker is carried on the frame
    // (FrameDiffer skips by SubtreeLength) AND written into the HTML as data-rask-opaque (the
    // client morph refuses to descend). Both are needed — the diff and the full-HTML fallback are
    // separate paths to the same DOM.
    protected virtual bool OpaqueSubtree => false;

    internal string? TagNameInternal => TagName;
    internal bool SelfClosingInternal => SelfClosing;
    internal bool OpaqueSubtreeInternal => OpaqueSubtree;

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

    // Hoisted into LiveState like Boundary: set on the live-render root and on every GetOrCreate'd child.
    // The guard keeps a `?? =` with a null handle — or a handle offered to a node that has no use for one
    // (OwnsRenderHandle) — from allocating a LiveState just to hold it.
    internal IRenderHandle? RenderHandle
    {
        get => _live?.RenderHandle;
        set
        {
            if (_live is null && (value is null || !OwnsRenderHandle))
            {
                return;
            }

            Live.RenderHandle = value;
        }
    }

    // Whether this node re-renders through a handle of its own: a component that can change its own state,
    // run a lifecycle, or own a handler. True unless a type says otherwise — the plain tags do (Element), which
    // is what keeps a mounted row of them from carrying a ~260 B LiveState apiece for a handle never read.
    private protected virtual bool OwnsRenderHandle => true;

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

    /// <summary>
    ///     What a routed page is called, for the layout around it to show — a breadcrumb, the document title:
    ///     <c>protected override string? PageTitle =&gt; _order is { } o ? $"Order {o.Number}" : null;</c>
    /// </summary>
    /// <remarks>
    ///     The layout reads it as <see cref="Routing.RouteState.Title" />, and the first HTML carries it: when
    ///     the title turns out to have changed, whatever showed it renders once more before anything is
    ///     sent. Read again on every render, so a title built from loaded data follows the data. <c>null</c>, the default, is a page with
    ///     no title of its own — the nearest layout above it that declares one stands in, and with none the
    ///     layout shows nothing. Only read on a component a route renders.
    /// </remarks>
    protected virtual string? PageTitle => null;

    internal string? PageTitleInternal => PageTitle;

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
    ///     <see cref="Dir.Rtl" /> for a right-to-left session culture and <c>null</c> otherwise, which emits
    ///     no attribute at all — left-to-right is HTML's own default, so writing it would only add bytes
    ///     to every page.
    /// </summary>
    protected virtual Dir? HtmlDir => Globalization.RaskCulture.HtmlDir;

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
        // The host's own attributes ride on the default shell — RaskApp and the WASM host put the UI kit's
        // theme scope here, so an app that draws with the kit is not grey. An override writes its own.
        Html.Lang(HtmlLang).Dir(HtmlDir).Attributes(LiveRenderContext.Current?.DocumentAttributes)[
            head, Body.Class(BodyClass)[body]];

    // The host's entry into the escape hatch above: RootErrorBoundary composes the document around the
    // App, so it needs to reach the App's override. Kept internal because Shell is a user-facing
    // extension point, not a call site — nothing outside the framework composes a document root.
    internal Component ShellInternal(Component head, Component body) => Shell(head, body);

    /// <summary>
    ///     How this component is rendered and transported — see <see cref="RenderEngine" />. Constant for the
    ///     session, so branching a <see cref="Render" /> on it is render-cache safe. See
    ///     <see cref="IsServer" /> / <see cref="IsWasm" />.
    /// </summary>
    protected static RenderEngine HostEngine => LiveRenderContext.CurrentSync?.Engine ?? RenderEngine.Server;

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
    protected static System.Globalization.CultureInfo Culture => Globalization.RaskCulture.Current;

    /// <summary>The language to render this component's text in. Marks ambient state, as <see cref="Culture" /> does.</summary>
    protected static System.Globalization.CultureInfo UICulture => Globalization.RaskCulture.CurrentUI;

    /// <summary>Whether the visitor's language is written right-to-left. Marks ambient state.</summary>
    protected static bool IsRightToLeft => Globalization.RaskCulture.IsRightToLeft;

    /// <summary><c>true</c> when rendered server-side over a live connection (<see cref="RenderEngine.Server" />).</summary>
    protected static bool IsServer => HostEngine == RenderEngine.Server;

    /// <summary><c>true</c> when rendered in the browser WebAssembly runtime (<see cref="RenderEngine.Wasm" />).</summary>
    protected static bool IsWasm => HostEngine == RenderEngine.Wasm;

    internal void WriteAttributesInternal(StringBuilder sb) => WriteAttributes(sb);
    internal IEnumerable<Component?> RenderChildrenInternal() => RenderChildren();
    internal IDisposable? EnterChildrenScopeInternal() => EnterChildrenScope();

    // Default: no HTML attributes. HTML element subclasses derive from Element, which
    // overrides this to emit id/class/style/data-*. Tag-specific overrides chain via
    // `base.WriteAttributes(sb)` so the universal attrs lead and tag-specific attrs follow.
    // Direct StringBuilder writes avoid the per-attribute KeyValuePair + iterator state-machine
    // allocations that the previous IEnumerable<KVP> shape forced on every render.
    protected virtual void WriteAttributes(StringBuilder sb) { }

    /// <summary>
    ///     Tells the devtools what this component's properties are. Machinery: an app neither calls nor writes this.
    /// </summary>
    /// <remarks>
    ///     The build writes the override, one per component, and ONLY where the devtools are on — a Release build emits
    ///     none of it, so a shipped app carries no description of its own state and pays nothing for this call. The
    ///     default does nothing, which is what every component does in a build without the tools.
    /// </remarks>
    /// <param name="describer">Collects the description.</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    protected internal virtual void DescribeProps(Diagnostics.DevTools.PropsDescriber describer) { }

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
        // Page.Render, so every unit test — is not. See #627.
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
}
